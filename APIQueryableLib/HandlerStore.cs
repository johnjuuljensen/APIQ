using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace APIQueryable;

public static class HandlerStore {
    interface IQueryExecutor {
        TResult Run<TResult>( IQueryContext<TResult> queryContext );
    }

    class QueryExecutor<T>: IQueryExecutor
        where T : class, IIsAPIQueryable<T> {
        public TResult Run<TResult>( IQueryContext<TResult> queryContext ) =>
            T.ExecuteQuery<TResult>( queryContext );
    }

    static readonly Dictionary<string, IQueryExecutor> ms_APIQueryableHandlers = new( StringComparer.InvariantCultureIgnoreCase );

    public static bool RegisterAPIQueryable<T>(string? nameOverload = null) where T : class, IIsAPIQueryable<T> {
        return ms_APIQueryableHandlers.TryAdd( nameOverload ?? typeof( T ).Name, new QueryExecutor<T>() );
    }

    public delegate TResult DMapNoHandler<TResult>();

    public static TResult InvokeAPIQHandler<TResult>( string entityName, IQueryContext<TResult> queryContext, DMapNoHandler<TResult> mapNoHandler ) =>
        ms_APIQueryableHandlers.TryGetValue( entityName, out var handler )
        ? handler.Run( queryContext )
        : mapNoHandler();

    public static IEnumerable<(string EntityName, TResult Result)> Select<TResult>( IQueryContext<TResult> queryContext ) =>
        ms_APIQueryableHandlers.OrderBy( kv => kv.Key ).Select( kv => (kv.Key, kv.Value.Run( queryContext )));
}

