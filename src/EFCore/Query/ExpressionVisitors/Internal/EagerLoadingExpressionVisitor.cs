// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Query.ResultOperators.Internal;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Parsing;

namespace Microsoft.EntityFrameworkCore.Query.ExpressionVisitors.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class EagerLoadingExpressionVisitor : RelinqExpressionVisitor
    {
        private readonly QueryCompilationContext _queryCompilationContext;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public EagerLoadingExpressionVisitor([NotNull] QueryCompilationContext queryCompilationContext)
            => _queryCompilationContext = queryCompilationContext;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override Expression VisitQuerySourceReference(QuerySourceReferenceExpression querySourceReferenceExpression)
        {
            if (_queryCompilationContext.QuerySourceRequiresMaterialization(querySourceReferenceExpression.ReferencedQuerySource))
            {
                var entityType = _queryCompilationContext.Model.FindEntityType(querySourceReferenceExpression.Type);

                var stack = new Stack<INavigation>();
                
                foreach (var navigation
                    in entityType.GetDeclaredNavigations()
                        .Where(
                            n => !n.IsDependentToPrincipal()
                                 && n.ForeignKey.IsOwnership))
                {
                    stack.Push(navigation);

                    GatherNavigations();

                    if (navigation.ForeignKey.IsEager)
                    {
                        _queryCompilationContext.AddAnnotations(
                            new[]
                            {
                                new IncludeResultOperator(navigation, querySourceReferenceExpression)
                            });
                    }
                }
            }

            return base.VisitQuerySourceReference(querySourceReferenceExpression);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override Expression VisitSubQuery(SubQueryExpression subQueryExpression)
        {
            Visit(subQueryExpression.QueryModel.SelectClause.Selector);

            return subQueryExpression;
        }
    }
}
