// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Utilities;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Clauses.ResultOperators;
using Remotion.Linq.Parsing;

namespace Microsoft.EntityFrameworkCore.Query.ExpressionVisitors.Internal
{
    public class NavigationRewritingExpressionVisitor : RelinqExpressionVisitor
    {
        private readonly EntityQueryModelVisitor _queryModelVisitor;
        private QueryModel _queryModel;

        public NavigationRewritingExpressionVisitor([NotNull] EntityQueryModelVisitor queryModelVisitor)
        {
            _queryModelVisitor = queryModelVisitor;
            Check.NotNull(queryModelVisitor, nameof(queryModelVisitor));
        }

        public virtual void Rewrite([NotNull] QueryModel queryModel)
        {
            Check.NotNull(queryModel, nameof(queryModel));

            _queryModel = queryModel;

            queryModel.TransformExpressions(Visit);
        }

//       (from o in context.Set<Order>()
//       where (from c in context.Set<Customer>()
//              where c.CustomerID == o.CustomerID
//              select c
//           ).First()
//           .City == "Seattle"
//       select o).ToList();

        protected override Expression VisitMember(MemberExpression memberExpression)
        {
            Check.NotNull(memberExpression, nameof(memberExpression));

            var newExpression
                = _queryModelVisitor.BindNavigationPathMemberExpression(
                    memberExpression,
                    (ps, qs) =>
                        {
                            var properties = ps.ToList();
                            var navigations = properties.OfType<INavigation>().ToList();

                            if (navigations.Any())
                            {
                                var outerQuerySourceReferenceExpression = new QuerySourceReferenceExpression(qs);
                                var targetType = navigations[0].GetTargetType().ClrType;

                                var mainFromClause
                                    = new MainFromClause(
                                        "n",
                                        targetType,
                                        Expression.Constant(CreateEntityQueryable(targetType)));

                                var selector = new SelectClause(new QuerySourceReferenceExpression(mainFromClause));

                                var subQueryModel = new QueryModel(mainFromClause, selector);

                                if (!navigations[0].IsCollection())
                                {
                                    subQueryModel.ResultOperators.Add(new FirstResultOperator(false));
                                }

                                return 
                                    Expression.MakeMemberAccess(
                                        new SubQueryExpression(subQueryModel), memberExpression.Member);
                            }

                            return default(Expression);
                        });

            return
                newExpression
                ?? base.VisitMember(memberExpression);
        }

        private static readonly IAsyncQueryProvider _queryProvider = new StubAsyncQueryProvider();

        private ConstantExpression CreateEntityQueryable(Type targetType)
            => Expression.Constant(
                _createEntityQueryableMethod
                    .MakeGenericMethod(targetType)
                    .Invoke(null, null));

        private static readonly MethodInfo _createEntityQueryableMethod
            = typeof(NavigationRewritingExpressionVisitor)
                .GetTypeInfo()
                .GetDeclaredMethod(nameof(_CreateEntityQueryable));

        [UsedImplicitly]
        private static EntityQueryable<TResult> _CreateEntityQueryable<TResult>()
            => new EntityQueryable<TResult>(_queryProvider);

        private class StubAsyncQueryProvider : IAsyncQueryProvider
        {
            public IQueryable CreateQuery(Expression expression)
            {
                throw new NotImplementedException();
            }

            public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
            {
                throw new NotImplementedException();
            }

            public object Execute(Expression expression)
            {
                throw new NotImplementedException();
            }

            public TResult Execute<TResult>(Expression expression)
            {
                throw new NotImplementedException();
            }

            public IAsyncEnumerable<TResult> ExecuteAsync<TResult>([NotNull] Expression expression)
            {
                throw new NotImplementedException();
            }

            public Task<TResult> ExecuteAsync<TResult>([NotNull] Expression expression, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }
        }

        #region Old

        public static bool IsCompositeKey([NotNull] Type type)
        {
            Check.NotNull(type, nameof(type));

            return type == typeof(CompositeKey);
        }

        private struct CompositeKey
        {
            public static bool operator ==(CompositeKey x, CompositeKey y) => x.Equals(y);
            public static bool operator !=(CompositeKey x, CompositeKey y) => !x.Equals(y);

            private readonly object[] _values;

            [UsedImplicitly]
            public CompositeKey(object[] values)
            {
                _values = values;
            }

            public override bool Equals(object obj)
                => _values.SequenceEqual(((CompositeKey)obj)._values);

            public override int GetHashCode() => 0;
        }

        #endregion
    }
}
