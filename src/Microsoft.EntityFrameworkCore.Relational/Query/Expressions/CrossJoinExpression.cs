// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq.Expressions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Query.Sql;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore.Query.Expressions
{
    /// <summary>
    ///     Represents a SQL CROSS JOIN expression.
    /// </summary>
    public class CrossJoinExpression : TableExpressionBase
    {
        private readonly TableExpressionBase _tableExpression;

        /// <summary>
        ///     Creates a new instance of a CrossJoinExpression.
        /// </summary>
        /// <param name="tableExpression"> The table expression. </param>
        public CrossJoinExpression([NotNull] TableExpressionBase tableExpression)
            : base(
                Check.NotNull(tableExpression, nameof(tableExpression)).QuerySource,
                tableExpression.Alias)
        {
            _tableExpression = tableExpression;
        }

        /// <summary>
        ///     The table expression.
        /// </summary>
        public virtual TableExpressionBase TableExpression => _tableExpression;

        protected override Expression Accept(ExpressionVisitor visitor)
        {
            Check.NotNull(visitor, nameof(visitor));

            var specificVisitor = visitor as ISqlExpressionVisitor;

            return specificVisitor != null
                ? specificVisitor.VisitCrossJoin(this)
                : base.Accept(visitor);
        }

        public override string ToString() => "CROSS JOIN " + _tableExpression;

        protected override Expression VisitChildren(ExpressionVisitor visitor)
        {
            visitor.Visit(_tableExpression);

            return this;
        }
    }
}
