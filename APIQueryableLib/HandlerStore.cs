using System;
using System.Collections.Generic;

namespace APIQueryable;

public static class HandlerStore {
    public interface IQueryExecutor {
        TResult Run<TResult>( IQueryContext<TResult> queryContext );
        Type EntityType { get; }
    }

    class QueryExecutor<T>: IQueryExecutor
        where T : class, IIsAPIQueryable<T> {
        public TResult Run<TResult>( IQueryContext<TResult> queryContext ) =>
            T.ExecuteQuery<TResult>( queryContext );

        public Type EntityType => typeof( T );
    }

    static readonly Dictionary<string, IQueryExecutor> ms_APIQueryableHandlers = new( StringComparer.InvariantCultureIgnoreCase );

    public static bool RegisterAPIQueryable<T>( string? nameOverload = null ) where T : class, IIsAPIQueryable<T> {
        return ms_APIQueryableHandlers.TryAdd( nameOverload ?? typeof( T ).Name, new QueryExecutor<T>() );
    }

    public delegate TResult DMapNoHandler<TResult>();

    public static TResult InvokeAPIQHandler<TResult>( string entityName, IQueryContext<TResult> queryContext, DMapNoHandler<TResult> mapNoHandler ) =>
        ms_APIQueryableHandlers.TryGetValue( entityName, out var handler )
        ? handler.Run( queryContext )
        : mapNoHandler();

    //public static IEnumerable<(string EntityName, TResult Result)> Select<TResult>( IQueryContext<TResult> queryContext ) =>
    //    ms_APIQueryableHandlers.OrderBy( kv => kv.Key ).Select( kv => (kv.Key, kv.Value.Run( queryContext )));

    public static IEnumerable<KeyValuePair<string, IQueryExecutor>> Handlers => ms_APIQueryableHandlers;
}

