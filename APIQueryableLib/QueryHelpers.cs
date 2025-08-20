using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace APIQueryable.Expressions;

public static class QueryHelpers {
    public static Expression<Func<T, object?>>[] GetIncludes<T>( this IQueryInclude<T> includesObject ) =>
        includesObject?.GetIncludeExpressions().SelectMany( _ => _ ).ToArray() ?? [];


    public static IEnumerable<Expression<Func<T, bool>>> GetExpressions<T, TConditions>( this IQueryCondition<T, TConditions> conds )
        where TConditions : IQueryCondition<T, TConditions> {
        var exprs = conds
            .GetLocalConditionExpressions()
            .Concat( conds.GetAssociationConditionExpressions().SelectMany( _ => _ ) );

        if ( conds.Or?.Any() ?? false ) {
            var orBlock = BooleanMerge(
                conds.Or.Select( andBlock => BooleanMerge( andBlock.GetExpressions(), Expression.AndAlso ) ),
                Expression.OrElse );

            exprs = exprs.Append( orBlock );
        }

        return exprs;
    }

    public static IEnumerable<Expression<Func<T, bool>>> GetConditionExpressionsAsAssociation<T, TA, TAConditions>(
        this IQueryCondition<TA, TAConditions>? conds, Expression<Func<T, TA>> assocMemberExpr )
        where TAConditions : IQueryCondition<TA, TAConditions>
        =>
        conds?.GetExpressions().GetExpressionsAsAssociation( assocMemberExpr ) ?? Enumerable.Empty<Expression<Func<T, bool>>>();


    public static IEnumerable<Expression<Func<T, object?>>> GetIncludeExpressionsFromAssociation<T, TA>(
        this IQueryInclude<TA>? includes,
        Expression<Func<T, TA>> assocMemberExpr,
        Expression<Func<T, object?>> assocAsObjMemberExpr ) {

        if ( includes == null ) {
            return [];
        }

        var nestedIncludes = includes.GetIncludeExpressions().SelectMany( _ => _ ).GetExpressionsAsAssociation( assocMemberExpr );
        if ( nestedIncludes.Any() ) {
            return nestedIncludes;
        }

        return new[] { assocAsObjMemberExpr };
    }


    static class ApplyExpressionToParentAssociation {
        public static Expression<Func<TC, TA>> Merge<TC, TB, TA>( Expression<Func<TC, TB>> cb, Expression<Func<TB, TA>> ba ) =>
            new Visitor<TC, TB, TA>( cb ).VisitAndMerge( ba );

        private class Visitor<TC, TB, TA>( Expression<Func<TC, TB>> parent ): ExpressionVisitor {
            internal Expression<Func<TC, TA>> VisitAndMerge( Expression<Func<TB, TA>> root ) =>
                (Expression<Func<TC, TA>>)VisitLambda( root );

            protected override Expression VisitLambda<T>( Expression<T> node ) =>
                Expression.Lambda<Func<TC, TA>>( Visit( node.Body ), parent.Parameters );

            protected override Expression VisitParameter( ParameterExpression node ) =>
                parent.Body;
        }
    }

    static IEnumerable<Expression<Func<T, TR>>> GetExpressionsAsAssociation<T, TA, TR>(
        this IEnumerable<Expression<Func<TA, TR>>> expressions, Expression<Func<T, TA>> assocMemberExpr )
        =>
        expressions?.Select( e => ApplyExpressionToParentAssociation.Merge( assocMemberExpr, e ) ) ?? [];





    class ReplaceParameterVisitor( ParameterExpression newParameter ): ExpressionVisitor {
        protected override Expression VisitParameter( ParameterExpression node ) {
            return newParameter;
        }
    }

    delegate Expression BooleanOp( Expression left, Expression right );

    static Expression<Func<T, bool>> BooleanMerge<T>( IEnumerable<Expression<Func<T, bool>>> es, BooleanOp op ) {
        var first = es.First();
        var parameter = first.Parameters[0];
        var visitor = new ReplaceParameterVisitor( parameter );

        Expression res = first.Body;
        foreach ( var e in es.Skip( 1 ) ) {
            var replacedExpr = (Expression<Func<T, bool>>)visitor.Visit( e );
            res = op( res, replacedExpr.Body );
        }

        return Expression.Lambda<Func<T, bool>>( res, parameter );
    }


    //public static Expression<Func<TEntity, T>> GetKeySelectorExpression<TEntity, T>(string propertyName)
    //{
    //    var parameter = Expression.Parameter(typeof(TEntity));

    //    //Check if property exist
    //    if (typeof(TEntity).GetProperty(propertyName) == null) throw new Exception("Invalid property name");

    //    var expression = Expression.Lambda<Func<TEntity, T>>(
    //        Expression.Convert(Expression.PropertyOrField(parameter, propertyName),
    //        typeof(T)), parameter);
    //    return expression;
    //}

    //public static Expression<Func<TSource, bool>> KeyPredicate<TSource, Tproperty>(Expression<Func<TSource, Tproperty>> keySelector, Tproperty comparisonValue)
    //{
    //    var bd = Expression.Equal(keySelector.Body, Expression.Constant(comparisonValue));
    //    return Expression.Lambda<Func<TSource, bool>>(bd, keySelector.Parameters);
    //}
}



//public abstract class QueryEntityDefaultHandlerAndConfig<THandler, TEntity, TConditions, TInclude> : IQueryEntityHandler, IQueryEntityConfig
//    where TEntity : class
//    where THandler : IQueryEntityHandler, new()
//    where TConditions : class, IQueryCondition<TEntity, TConditions>
//    where TInclude : class, IQueryInclude<TEntity>
//{
//    public virtual string HandlerName => typeof(TEntity).Name;

//    public TConfig RunConfig<TConfig>(IQueryEntityConfigVisitor<TConfig> visitor) =>
//        visitor.CreateConfig<TEntity, TConditions, TInclude, THandler>(HandlerName, typeof(Expression<Func<TEntity, object>>[]));

//    public IEnumerable Handle(IQueryContext context, out int? count) => context.ExecuteQuery<TEntity, TConditions, TInclude>(out count);
//}
