// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq.Expressions;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query.Internal;
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
        private static readonly PropertyInfo _queryContextContextPropertyInfo
            = typeof(QueryContext)
                .GetTypeInfo()
                .GetDeclaredProperty(nameof(QueryContext.Context));

        private readonly IMaterializerFactory _materializerFactory;
        private readonly IModel _model;
        private readonly IQuerySource _querySource;
        private readonly IQueryCompiler _queryCompiler;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public InMemoryEntityQueryableExpressionVisitor(
            [NotNull] IModel model,
            [NotNull] IMaterializerFactory materializerFactory,
            [NotNull] EntityQueryModelVisitor entityQueryModelVisitor,
            [NotNull] IQueryCompiler queryCompiler,
            [CanBeNull] IQuerySource querySource)
            : base(Check.NotNull(entityQueryModelVisitor, nameof(entityQueryModelVisitor)))
        {
            Check.NotNull(model, nameof(model));
            Check.NotNull(materializerFactory, nameof(materializerFactory));
            Check.NotNull(queryCompiler, nameof(queryCompiler));

            _model = model;
            _materializerFactory = materializerFactory;
            _querySource = querySource;
            _queryCompiler = queryCompiler;
        }

        private new InMemoryQueryModelVisitor QueryModelVisitor
            => (InMemoryQueryModelVisitor) base.QueryModelVisitor;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override Expression VisitEntityQueryable(Type elementType)
        {
            Check.NotNull(elementType, nameof(elementType));

            var entityType = QueryModelVisitor.QueryCompilationContext.FindEntityType(_querySource)
                             ?? _model.FindEntityType(elementType);

            if (entityType.IsViewType())
            {
                var query = entityType.FindAnnotation("query");

                if (query != null)
                {
                    return Expression.Invoke(
                        Expression.Constant(query.Value),
                        Expression.Property(
                            EntityQueryModelVisitor.QueryContextParameter,
                            _queryContextContextPropertyInfo));
                }
                throw new InvalidOperationException();
            }

            if (QueryModelVisitor.QueryCompilationContext
                .QuerySourceRequiresMaterialization(_querySource))
            {
                var materializer = _materializerFactory.CreateMaterializer(entityType);

                return Expression.Call(
                    InMemoryQueryModelVisitor.EntityQueryMethodInfo.MakeGenericMethod(elementType),
                    EntityQueryModelVisitor.QueryContextParameter,
                    Expression.Constant(entityType),
                    Expression.Constant(entityType.FindPrimaryKey()),
                    materializer,
                    Expression.Constant(QueryModelVisitor.QueryCompilationContext.IsTrackingQuery));
            }

            return Expression.Call(
                InMemoryQueryModelVisitor.ProjectionQueryMethodInfo,
                EntityQueryModelVisitor.QueryContextParameter,
                Expression.Constant(entityType));
        }
    }
}