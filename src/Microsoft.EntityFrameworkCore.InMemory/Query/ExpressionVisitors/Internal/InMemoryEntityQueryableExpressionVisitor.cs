// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Utilities;
using Remotion.Linq.Clauses;

namespace Microsoft.EntityFrameworkCore.Query.ExpressionVisitors.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class InMemoryEntityQueryableExpressionVisitor : EntityQueryableExpressionVisitor
    {
        private readonly IModel _model;
        private readonly IMaterializerFactory _materializerFactory;
        private readonly IQuerySource _querySource;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public InMemoryEntityQueryableExpressionVisitor(
            [NotNull] IModel model,
            [NotNull] IMaterializerFactory materializerFactory,
            [NotNull] EntityQueryModelVisitor entityQueryModelVisitor,
            [CanBeNull] IQuerySource querySource)
            : base(Check.NotNull(entityQueryModelVisitor, nameof(entityQueryModelVisitor)))
        {
            Check.NotNull(model, nameof(model));
            Check.NotNull(materializerFactory, nameof(materializerFactory));

            _model = model;
            _materializerFactory = materializerFactory;
            _querySource = querySource;
        }

        private new InMemoryQueryModelVisitor QueryModelVisitor
            => (InMemoryQueryModelVisitor)base.QueryModelVisitor;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override Expression VisitEntityQueryable(Type elementType)
        {
            Check.NotNull(elementType, nameof(elementType));

            var entityType = _model.FindEntityType(elementType);

            var valueBufferFilter
                = (Expression)QueryModelVisitor.TryCreateEntityFilter(entityType, _querySource)
                  ?? Expression.Constant(null, typeof(Func<ValueBuffer, bool>));

            if (QueryModelVisitor.QueryCompilationContext
                .QuerySourceRequiresMaterialization(_querySource))
            {
                var materializer = _materializerFactory.CreateMaterializer(entityType);

                var expression
                    = Expression.Call(
                        _entityQueryMethodInfo.MakeGenericMethod(elementType),
                        Expression.Convert(EntityQueryModelVisitor.QueryContextParameter, typeof(InMemoryQueryContext)),
                        Expression.Constant(entityType),
                        Expression.Constant(entityType.FindPrimaryKey()),
                        materializer,
                        Expression.Constant(QueryModelVisitor.QueryCompilationContext.IsTrackingQuery),
                        valueBufferFilter);

                return expression;
            }

            return Expression.Call(
                _projectionQueryMethodInfo,
                Expression.Convert(EntityQueryModelVisitor.QueryContextParameter, typeof(InMemoryQueryContext)),
                Expression.Constant(entityType),
                valueBufferFilter);
        }

        private static readonly MethodInfo _entityQueryMethodInfo
            = typeof(InMemoryEntityQueryableExpressionVisitor).GetTypeInfo()
                .GetDeclaredMethod(nameof(EntityQuery));

        [UsedImplicitly]
        private static IEnumerable<TEntity> EntityQuery<TEntity>(
            InMemoryQueryContext queryContext,
            IEntityType entityType,
            IKey key,
            Func<IEntityType, ValueBuffer, object> materializer,
            bool queryStateManager,
            Func<ValueBuffer, bool> filter)
            where TEntity : class
            => queryContext.Store
                .GetTables(entityType)
                .SelectMany(t =>
                    t.Rows
                        .Select(vs => new ValueBuffer(vs))
                        .Where(vb => filter == null || filter(vb))
                        .Select(vb =>
                            (TEntity)queryContext
                                .QueryBuffer
                                .GetEntity(
                                    key,
                                    new EntityLoadInfo(
                                        vb,
                                        vr => materializer(t.EntityType, vr)),
                                    queryStateManager,
                                    throwOnNullKey: false)));

        private static readonly MethodInfo _projectionQueryMethodInfo
            = typeof(InMemoryEntityQueryableExpressionVisitor).GetTypeInfo()
                .GetDeclaredMethod(nameof(ProjectionQuery));

        [UsedImplicitly]
        private static IEnumerable<ValueBuffer> ProjectionQuery(
            InMemoryQueryContext queryContext,
            IEntityType entityType,
            Func<ValueBuffer, bool> filter)
            => queryContext.Store
                .GetTables(entityType)
                .SelectMany(t => t.Rows)
                .Select(vs => new ValueBuffer(vs))
                .Where(vb => filter == null || filter(vb));
    }
}
