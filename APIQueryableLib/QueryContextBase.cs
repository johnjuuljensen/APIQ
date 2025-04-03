using System;
using System.Linq;
using System.Linq.Expressions;

using APIQueryable.Expressions;

namespace APIQueryable;

public abstract class QueryContextBase<TResult>: IQueryContext<TResult> {
    public class Request<TConditions, TInclude>
        where TConditions : class
        where TInclude : class {
        public TConditions? Where { get; set; }
        public TInclude? Include { get; set; }
        public int? Limit { get; set; }
    }

    protected abstract Request<TConditions, TInclude>? ParseQuery<TEntity, TConditions, TInclude>()
        where TEntity : class
        where TConditions : class, IQueryCondition<TEntity, TConditions>
        where TInclude : class, IQueryInclude<TEntity>;

    protected abstract TResult MapParseError<TEntity>();
    protected abstract TResult MapResult<TEntity>( IQueryable<TEntity> entities );

    public delegate IQueryable<TEntity> DGetQueryable<TEntity>( Expression<Func<TEntity, object?>>[] withExprs ) where TEntity : class;
    protected abstract IQueryable<TEntity> GetQueryable<TEntity>( params Expression<Func<TEntity, object?>>[] withExprs ) where TEntity : class;

    protected abstract int DefaultLimit { get; }

    public TResult ExecuteQuery<TEntity, TConditions, TInclude>( DGetQueryable<TEntity> getQueryable )
        where TEntity : class
        where TConditions : class, IQueryCondition<TEntity, TConditions>
        where TInclude : class, IQueryInclude<TEntity> {

        Request<TConditions, TInclude>? req;

        try {
            req = ParseQuery<TEntity, TConditions, TInclude>();
            if ( req == null ) return MapParseError<TEntity>();
        } catch ( Exception ) {
            return MapParseError<TEntity>();
        }

        var entities = getQueryable( req.Include?.GetIncludes() ?? [] );

        foreach ( var condExpr in req.Where?.GetExpressions() ?? [] ) {
            entities = entities.Where( condExpr );
        }

        entities = entities.Take( req.Limit ?? DefaultLimit );

        return MapResult( entities );
    }

    public TResult ExecuteQuery<TEntity, TConditions, TInclude>()
        where TEntity : class
        where TConditions : class, IQueryCondition<TEntity, TConditions>
        where TInclude : class, IQueryInclude<TEntity>
        => ExecuteQuery<TEntity, TConditions, TInclude>( GetQueryable );
}




    /*
    About projection.

    There's no particularly good reason to support projections other than excluding fields from serialization. Any other projections
    could just as well be performed by the calling party.

    Joins:

    Joins do make sense, but only in unprojected form. 
    A join could be returned as a list of pairs.
    The join should be performed at store level, otherwise it's just two subsequent dependent queries that might as well
    be handled by the calling party. 
    For the version with seperate queries it might make sense to support multiple queries in a request to reduce roundtrip costs.
    */


    //public static IQueryable<TEntity> BuildQuery<TEntity, TConditions, TInclude>(IQueryable<TEntity> entities, TConditions? conditions )
    //    //, int? limit, out int? count, int? offset = null, string? orderBy = null)
    //    where TConditions : class, IQueryCondition<TEntity, TConditions>
    //    where TInclude : class, IQueryInclude<TEntity>
    //{

    //    //count = limit.HasValue && offset.HasValue ? entities.Count() : null;

    //    //if (!string.IsNullOrWhiteSpace(orderBy)) entities = entities.OrderBy(QueryHelpers.GetKeySelectorExpression<TEntity, object>(orderBy));

    //    //if (offset.HasValue && offset.Value > 0) entities = entities.Skip((limit ?? 1) * (offset.Value - 1));

    //    //if (limit.HasValue) entities = entities.Take(limit.Value);

    //    return entities;
    //}
