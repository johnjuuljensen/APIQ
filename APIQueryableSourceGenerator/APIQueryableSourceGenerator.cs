using System.Collections.Immutable;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using SourceGeneratorCommon;

namespace APIQueryableSourceGenerator;

record GlobalConfig(
    //string UpdatableInterface,
    //string UpdatableInterfaceCloneMethod,
    //EquatableArray<string> UsingNamespaces,
    //string ChangeTrackerType,
    //string ChangeTrackerSetPropertyMethod
    ) {
    const string OPTIONS_PREFIX = nameof( APIQueryableSourceGenerator );
    public static GlobalConfig Load( AnalyzerConfigOptions opt ) =>
        new GlobalConfig(
            //UpdatableInterface: opt.GetStringOrDefault( OPTIONS_PREFIX, nameof( UpdatableInterface ) ) ?? "IUpdatable",
            //UpdatableInterfaceCloneMethod: opt.GetStringOrDefault( OPTIONS_PREFIX, nameof( UpdatableInterfaceCloneMethod ) ) ?? "CloneForUpdate",
            //UsingNamespaces: (opt.GetStringOrDefault( OPTIONS_PREFIX, nameof( UsingNamespaces ) )
            //    ?.Split( new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries ) ?? [])
            //    .Select( _ => _.Trim() )
            //    .Where( _ => !string.IsNullOrWhiteSpace( _ ) )
            //    .ToImmutableArray(),
            //ChangeTrackerType: opt.GetStringOrDefault( OPTIONS_PREFIX, nameof( ChangeTrackerType ) ) ?? "ChangeTracker",
            //ChangeTrackerSetPropertyMethod: opt.GetStringOrDefault( OPTIONS_PREFIX, nameof( ChangeTrackerSetPropertyMethod ) ) ?? "SetProperty"
        );

    public static GlobalConfig Load( AnalyzerConfigOptionsProvider optProv, CancellationToken _ ) =>
        Load( optProv.GlobalOptions );
}

public static partial class CodeAnalysisExtensions {
    public static bool IsNullable( this ITypeSymbol typeSymbol ) =>
        typeSymbol.NullableAnnotation == NullableAnnotation.Annotated;

    public static bool IsNullableValueType( this ITypeSymbol typeSymbol ) =>
        typeSymbol.IsValueType && typeSymbol.IsNullable();

    public static bool TryGetNullableValueUnderlyingType( this ITypeSymbol typeSymbol, out ITypeSymbol? underlyingType ) {
        if ( typeSymbol is INamedTypeSymbol namedType && typeSymbol.IsNullableValueType() && namedType.IsGenericType ) {
            var typeParameters = namedType.TypeArguments;
            // Assert the generic is named System.Nullable<T> as expected.
            underlyingType = typeParameters[0];
            // TODO: decide what to return when the underlying type is not declared due to some compilation error.
            // TypeKind.Error indicats a compilation error, specifically a nullable type where the underlying type was not found.
            // I have observed that IsValueType will be true in such cases even though it is actually unknown whether the missing type is a value type
            // I chose to return false but you may prefer something else. 
            return underlyingType.TypeKind == TypeKind.Error ? false : true;
        }
        underlyingType = null;
        return false;
    }

    public static bool IsEnum( this ITypeSymbol typeSymbol ) =>
        typeSymbol is INamedTypeSymbol namedType && namedType.EnumUnderlyingType != null;

    public static bool IsNullableEnumType( this ITypeSymbol typeSymbol ) =>
        typeSymbol.TryGetNullableValueUnderlyingType( out var underlyingType ) == true && (underlyingType?.IsEnum() ?? false);
}

record Property(
    string Name,
    bool IsValueType,
    SpecialType SpecialType,
    //string Type,
    string TypeWithoutNullable,
    bool TypeIsNullable,
    bool IsVirtual
    );

record APIQClassConfig(
    string ContainingNamespace,
    string Name,
    EquatableArray<Property> RelevantProperties,
    string? TypescriptPath
    );

[Generator]
public class IncrementalGenerator: IIncrementalGenerator {

    static bool IsCOWObjectCandidate( SyntaxNode s, CancellationToken t ) =>
        s is ClassDeclarationSyntax cls
        && (cls.Modifiers.Any( m => m.IsKind( SyntaxKind.PublicKeyword ) ) || cls.Modifiers.Any( m => m.IsKind( SyntaxKind.InternalKeyword ) ))
        && cls.Modifiers.Any( m => m.IsKind( SyntaxKind.PartialKeyword ) )
        && !cls.Modifiers.Any( m => m.IsKind( SyntaxKind.StaticKeyword ) );


    public void Initialize( IncrementalGeneratorInitializationContext initContext ) {
        var configProv = initContext.AnalyzerConfigOptionsProvider.Select( GlobalConfig.Load );

        // Collect classes with the attribute
        var cowClasses = initContext.SyntaxProvider
            .ForAttributeWithMetadataName(
                "APIQueryable.APIQueryableAttribute",
                ( _, _ ) => true,
                ( context, _ ) => {
                    var clsSymbol = (INamedTypeSymbol)context.TargetSymbol;
                    var queryProps = clsSymbol.GetMembers()
                        .OfType<IPropertySymbol>()
                        .Where( _ => _.DeclaredAccessibility == Accessibility.Public
                            && !_.GetAttributes().Any( a => a.AttributeClass?.Name == "TenantKeyAttribute" )
                            && !_.GetAttributes().Any( a => a.AttributeClass?.Name == "JsonIgnoreAttribute" )
                            )
                        .Select( p => {
                            var (propType, isNullableValueType) = p.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                                ? (((INamedTypeSymbol)p.Type).TypeArguments[0], true)
                                : (p.Type, false);

                            return new Property(
                                Name: p.Name,
                                IsValueType: propType.IsValueType,
                                SpecialType: (propType.IsEnum() || propType.IsNullableEnumType()) ? SpecialType.System_Enum : propType.SpecialType,
                                //Type: propType.ToDisplayString( SymbolDisplayFormat.MinimallyQualifiedFormat ),
                                TypeWithoutNullable: propType.WithNullableAnnotation( NullableAnnotation.NotAnnotated ).ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                                TypeIsNullable: p.NullableAnnotation == NullableAnnotation.Annotated || isNullableValueType,
                                IsVirtual: p.IsVirtual
                            );
                        } )
                        .ToImmutableArray();


                    return new APIQClassConfig(
                        ContainingNamespace: clsSymbol.ContainingNamespace.Name,
                        Name: clsSymbol.Name,
                        RelevantProperties: new( queryProps ),
                        TypescriptPath:
                            clsSymbol.Locations
                                .Where( _ => _.IsInSource )
                                .OrderByDescending( _ => (_.SourceTree?.FilePath?.Contains( ".g." ) ?? false) || (_.SourceTree?.FilePath?.Contains( ".generated." ) ?? false) )
                                .FirstOrDefault()
                                ?.SourceTree?.FilePath is string sourceFilePath
                            ? Path.Combine( Path.GetDirectoryName( sourceFilePath ), "typescript", $"{clsSymbol.Name}.apiq.d.ts" )
                            : ""
                        );
                } );

        var globalConfig = initContext.AnalyzerConfigOptionsProvider
            .Select( GlobalConfig.Load );

        initContext.RegisterSourceOutput(
            (IncrementalValuesProvider<(APIQClassConfig Left, GlobalConfig Right)>)IncrementalValueProviderExtensions.Combine<APIQClassConfig, GlobalConfig>( cowClasses, globalConfig ),
            ( spc, ivp ) => {
                APIQClassConfig classConfig = ivp.Left;
                GlobalConfig globalConfig = ivp.Right;
                GenerateSource( spc, globalConfig, classConfig );
            } );
    }

    static IReadOnlyDictionary<string, string> Query_ValueTypeConditions { get; } = new Dictionary<string, string> {
        // operators are defined with condition being rhs:
        { "Greater", ">"},
        { "GreaterEqual", ">=" },
        { "Less", "<" },
        { "LessEqual", "<=" },
    };


    private void GenerateSource( SourceProductionContext context, GlobalConfig conf, APIQClassConfig cls ) {
        StringBuilder builder = new();

        StringBuilder usings = new();
        //foreach ( var ns in conf.UsingNamespaces ) {
        //    usings.AppendLine( $"using {ns};" );
        //}

        /*



        if ( !clsAttr.SkipRegistration ) {
            builder.Append( $@"
public partial class QueryEntity_{ClsName}: QueryEntityDefaultHandlerAndConfig<QueryEntity_{ClsName}, {ClsName}, {clsNameOverride}_Conditions, {clsNameOverride}_Includes> {{ }}
" );
        }

        */

        const string propIndent = "    ";
        const string exprIndent = "        ";

        var assocProps =
            from prop in cls.RelevantProperties
            where
                prop.IsVirtual
            select prop;

        StringBuilder tsIncludeProps = new();
        StringBuilder includeProps = new();
        StringBuilder includeExprs = new();
        StringBuilder assocConditionProps = new();
        StringBuilder assocConditionExprs = new();
        foreach ( var _ in assocProps ) {
            tsIncludeProps.AppendLine( $"{propIndent}readonly {_.Name}?: {_.Name}_Includes;" );
            includeProps.AppendLine( $"{propIndent}public {_.TypeWithoutNullable}_Includes? {_.Name} {{ get; init; }}" );
            includeExprs.AppendLine( $"{exprIndent}yield return {_.Name}?.GetIncludeExpressionsFromAssociation( {cls.Name}Ext.{_.Name}_NotNullExpr, {cls.Name}Ext.{_.Name}_ObjExpr ) ?? [];" );

            assocConditionProps.AppendLine( $"{propIndent}public {_.TypeWithoutNullable}_Conditions? {_.Name} {{ get; init; }}" );
            assocConditionExprs.AppendLine( $"{exprIndent}yield return {_.Name}?.GetConditionExpressionsAsAssociation( {cls.Name}Ext.{_.Name}_NotNullExpr ) ?? [];" );
        }

        var dataProps =
            from prop in cls.RelevantProperties
            where
                !prop.IsVirtual
            select prop;

        static string TsType( in string t ) =>
            t.ToUpperInvariant() switch {
                "INT" or "UINT" or "FLOAT" or "DOUBLE" or "LONG" or "ULONG" => "number",
                "INSTANT" or "DATETIME" or "DATETIMEOFFSET" => "Date",
                "BOOL" => "boolean",
                _ => t
            };


        StringBuilder tsConditionProps = new();
        StringBuilder conditionProps = new();
        StringBuilder conditionExprs = new();
        foreach ( var prop in dataProps ) {
            bool hasIn = true;

            if ( prop.SpecialType == SpecialType.System_DateTime ) {
                hasIn = false;
            }

            if ( prop.IsValueType
                && prop.SpecialType != SpecialType.System_Boolean
                && prop.SpecialType != SpecialType.System_Enum
                && !prop.Name.EndsWith("Id")
                //&& !propType.IsGenericType
                //&& !SymbolEqualityComparer.Default.Equals( propType, IdT )
                //&& propType.AllInterfaces.Contains( ComparableT.Construct( propType ), SymbolEqualityComparer.Default ) 
            ) {
                foreach ( var kv in Query_ValueTypeConditions ) {
                    tsConditionProps.AppendLine( $"{propIndent}readonly {prop.Name}{kv.Key}?: {TsType(prop.TypeWithoutNullable)}; // {prop.SpecialType}" );
                    conditionProps.AppendLine( $"{propIndent}public {prop.TypeWithoutNullable}? {prop.Name}{kv.Key} {{ get; init; }}" );
                    conditionExprs.AppendLine( $"{exprIndent}if ( {prop.Name}{kv.Key}.HasValue ) yield return _ => _.{prop.Name} {kv.Value} {prop.Name}{kv.Key};" );
                }
            }

            if ( prop.SpecialType == SpecialType.System_String ) {
                tsConditionProps.AppendLine( $"{propIndent}readonly {prop.Name}StartsWith?: string;" );
                conditionProps.AppendLine( $"{propIndent}public string? {prop.Name}StartsWith {{ get; init; }}" );
                conditionExprs.AppendLine( $"{exprIndent}if ( {prop.Name}StartsWith?.Length > 0 ) yield return _ => _.{prop.Name}!.StartsWith( {prop.Name}StartsWith );" );
            }

            if ( prop.SpecialType == SpecialType.System_Boolean ) {
                hasIn = false;
                tsConditionProps.AppendLine( $"{propIndent}readonly {prop.Name}?: boolean;" );
                conditionProps.AppendLine( $"{propIndent}public bool? {prop.Name} {{ get; init; }}" );
                conditionExprs.AppendLine( $"{exprIndent}if ( {prop.Name}.HasValue) yield return _ => _.{prop.Name} == {prop.Name};" );
            }

            if ( hasIn ) {
                tsConditionProps.AppendLine( $"{propIndent}readonly {prop.Name}In?: ReadonlyArray<{TsType(prop.TypeWithoutNullable)}>;" );
                conditionProps.AppendLine( $"{propIndent}public IReadOnlyCollection<{prop.TypeWithoutNullable}>? {prop.Name}In {{ get; init; }}" );
                conditionExprs.AppendLine( $"{exprIndent}if ( {prop.Name}In?.Count > 0 ) yield return _ => {prop.Name}In.Contains( _.{prop.Name}{(prop.IsValueType && prop.TypeIsNullable ? "!.Value" : "")} );" );
            }

            if ((prop.IsValueType || prop.SpecialType == SpecialType.System_String) && prop.TypeIsNullable) {
                tsConditionProps.AppendLine( $"{propIndent}readonly {prop.Name}IsNull?: boolean;" );
                conditionProps.AppendLine( $"{propIndent}public bool? {prop.Name}IsNull {{ get; init; }}" );
                conditionExprs.AppendLine( $"{exprIndent}if ( {prop.Name}IsNull.HasValue ) yield return _ => (_.{prop.Name} == null) == {prop.Name}IsNull;" );
            }
        }

        // Generate the partial class with ForTest method
        builder.AppendLine( $$"""
        #nullable enable
        {{usings}}

        namespace {{cls.ContainingNamespace}};

        public partial class {{cls.Name}}: IIsAPIQueryable<{{cls.Name}}>
        {
            public static TResult ExecuteQuery<TResult>( IQueryContext<TResult> queryContext ) =>
                queryContext.ExecuteQuery<{{cls.Name}}, {{cls.Name}}_Conditions, {{cls.Name}}_Includes>();
        }

        public partial class {{cls.Name}}_Includes: IQueryInclude<{{cls.Name}}> {
        {{includeProps}}
        
            public IEnumerable<IEnumerable<Expression<Func<{{cls.Name}}, object?>>>> GetIncludeExpressions() {
        {{includeExprs}}
                yield break;
            }
        }
        

        public partial class {{cls.Name}}_Conditions: IQueryCondition<{{cls.Name}}, {{cls.Name}}_Conditions>
        {
        {{conditionProps}}

            public IEnumerable<Expression<Func<{{cls.Name}}, bool>>> GetLocalConditionExpressions() {
        {{conditionExprs}}
                yield break;
            }

        {{assocConditionProps}}

            public IEnumerable<IEnumerable<Expression<Func<{{cls.Name}}, bool>>>> GetAssociationConditionExpressions() {
        {{assocConditionExprs}}
                yield break;
            }

            public IEnumerable<{{cls.Name}}_Conditions>? Or { get; init; }
        }
        """ );

        context.AddSource( $"{cls.Name}.apiq.g.cs", SourceText.From( builder.ToString(), Encoding.UTF8 ) );

#pragma warning disable RS1035
        if ( cls.TypescriptPath is not null ) {

            StringBuilder ts = new();
            ts.AppendLine( $$"""
            import type { {{cls.Name}} } from './{{cls.Name}}.d.ts';
            """ );
            HashSet<string> imports = new() { cls.Name };
            foreach ( var p in cls.RelevantProperties.Where( _ => _.IsVirtual || _.SpecialType == SpecialType.System_Enum ) ) {
                if ( !imports.Contains( p.TypeWithoutNullable ) ) {
                    string path = p.SpecialType == SpecialType.System_Enum ? ".." : ".";
                    ts.AppendLine( $$"""import type { {{p.TypeWithoutNullable}} } from '{{path}}/{{p.TypeWithoutNullable}}.d.ts'""" );
                    imports.Add( p.TypeWithoutNullable );
                }
            }

            ts.AppendLine( $$"""

            interface {{cls.Name}}_Includes {
            """ );

            ts.Append( tsIncludeProps );

            ts.AppendLine( $$"""
            }

            """ );


            ts.AppendLine( $$"""

            interface {{cls.Name}}_Conditions {
            """ );

            ts.Append( tsConditionProps );

            ts.AppendLine( $$"""
            }

            """ );

            Directory.CreateDirectory( Path.GetDirectoryName( cls.TypescriptPath ) );

            using var fs = new FileStream( cls.TypescriptPath, FileMode.Create, FileAccess.Write );
            using var s = new StreamWriter( fs, Encoding.UTF8 );
            s.Write( ts );

            s.Close();
            fs.Close();
        }
#pragma warning restore RS1035

    }
}