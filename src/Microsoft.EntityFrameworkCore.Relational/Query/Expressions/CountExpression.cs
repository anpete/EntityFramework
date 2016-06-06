// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq.Expressions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Query.Sql;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore.Query.Expressions
{
    /// <summary>
    ///     Represents a SQL COUNT expression.
    /// </summary>
    public class CountExpression : Expression
    {
        /// <summary>
        ///     Creates a new instance of a CountExpression.
        /// </summary>
        public CountExpression()
            : this(typeof(int))
        {
        }

        /// <summary>
        ///     Creates a new instance of a CountExpression.
        /// </summary>
        /// <param name="type"> The type. </param>
        public CountExpression([NotNull] Type type)
        {
            Type = type;
        }

        public override ExpressionType NodeType => ExpressionType.Extension;

        public override Type Type { get; }

        protected override Expression Accept(ExpressionVisitor visitor)
        {
            Check.NotNull(visitor, nameof(visitor));

            var specificVisitor = visitor as ISqlExpressionVisitor;

            return specificVisitor != null
                ? specificVisitor.VisitCount(this)
                : base.Accept(visitor);
        }

        protected override Expression VisitChildren(ExpressionVisitor visitor) => this;
    }
}
