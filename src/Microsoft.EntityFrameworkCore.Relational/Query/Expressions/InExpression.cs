// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Query.Sql;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore.Query.Expressions
{
    /// <summary>
    ///     Represents a SQL IN expression.
    /// </summary>
    public class InExpression : Expression
    {
        /// <summary>
        ///     Creates a new instance of InExpression.
        /// </summary>
        /// <param name="operand"> The operand. </param>
        /// <param name="values"> The values. </param>
        public InExpression(
            [NotNull] AliasExpression operand,
            [NotNull] IReadOnlyList<Expression> values)
        {
            Check.NotNull(operand, nameof(operand));
            Check.NotNull(values, nameof(values));

            Operand = operand;
            Values = values;
        }

        /// <summary>
        ///     Creates a new instance of InExpression.
        /// </summary>
        /// <param name="operand"> The operand. </param>
        /// <param name="subQuery"> The sub query. </param>
        public InExpression(
            [NotNull] AliasExpression operand,
            [NotNull] SelectExpression subQuery)
        {
            Check.NotNull(operand, nameof(operand));
            Check.NotNull(subQuery, nameof(subQuery));

            Operand = operand;
            SubQuery = subQuery;
        }

        /// <summary>
        ///     Gets the operand.
        /// </summary>
        /// <value>
        ///     The operand.
        /// </value>
        public virtual AliasExpression Operand { get; }

        /// <summary>
        ///     Gets the values.
        /// </summary>
        /// <value>
        ///     The values.
        /// </value>
        public virtual IReadOnlyList<Expression> Values { get; }

        /// <summary>
        ///     Gets the sub query.
        /// </summary>
        /// <value>
        ///     The sub query.
        /// </value>
        public virtual SelectExpression SubQuery { get; }

        public override ExpressionType NodeType => ExpressionType.Extension;

        public override Type Type => typeof(bool);

        protected override Expression Accept(ExpressionVisitor visitor)
        {
            Check.NotNull(visitor, nameof(visitor));

            var specificVisitor = visitor as ISqlExpressionVisitor;

            return specificVisitor != null
                ? specificVisitor.VisitIn(this)
                : base.Accept(visitor);
        }

        protected override Expression VisitChildren(ExpressionVisitor visitor) => this;

        public override string ToString()
            => Operand.Expression + " IN (" + string.Join(", ", Values) + ")";
    }
}
