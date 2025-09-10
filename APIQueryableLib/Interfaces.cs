using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace APIQueryable;

public class APIQueryableAttribute: Attribute {
    public bool SkipRegistration { get; set; }
    public bool Exclude { get; set; }
}


public interface IQueryInclude<T> {
    IEnumerable<IEnumerable<Expression<Func<T, object?>>>> GetIncludeExpressions();
}


public interface IQueryCondition<T, TConditions> where TConditions : IQueryCondition<T, TConditions> {
    IEnumerable<Expression<Func<T, bool>>> GetLocalConditionExpressions();
    IEnumerable<IEnumerable<Expression<Func<T, bool>>>> GetAssociationConditionExpressions();

    IEnumerable<TConditions>? Or { get; }

    IEnumerable<Expression<Func<T, bool>>> GetCustomConditionExpressions() => Enumerable.Empty<Expression<Func<T, bool>>>();
}


public interface IIsAPIQueryable { }


public interface IIsAPIQueryable<TEntity>: IIsAPIQueryable {
    static abstract TResult ExecuteQuery<TResult>( IQueryContext<TResult> queryContext );
}


public interface IQueryContext<TResult> {
    TResult ExecuteQuery<TEntity, TConditions, TInclude>()
        where TEntity : class
        where TConditions : class, IQueryCondition<TEntity, TConditions>
        where TInclude : class, IQueryInclude<TEntity>;
}
