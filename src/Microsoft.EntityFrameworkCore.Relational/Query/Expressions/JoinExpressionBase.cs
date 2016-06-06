// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq.Expressions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore.Query.Expressions
{
    /// <summary>
    ///     A base class for SQL JOIN expressions.
    /// </summary>
    public abstract class JoinExpressionBase : TableExpressionBase
    {
        private readonly TableExpressionBase _tableExpression;
        private Expression _predicate;

        /// <summary>
        ///     Specialised constructor for use only by derived class.
        /// </summary>
        /// <param name="tableExpression"> The target table expression. </param>
        protected JoinExpressionBase([NotNull] TableExpressionBase tableExpression)
            : base(
                Check.NotNull(tableExpression, nameof(tableExpression)).QuerySource,
                tableExpression.Alias)
        {
            _tableExpression = tableExpression;
        }

        /// <summary>
        ///     The target table expression.
        /// </summary>
        public virtual TableExpressionBase TableExpression => _tableExpression;

        /// <summary>
        ///     Gets or sets the predicate.
        /// </summary>
        /// <value>
        ///     The predicate.
        /// </value>
        public virtual Expression Predicate
        {
            get { return _predicate; }
            [param: NotNull]
            set
            {
                Check.NotNull(value, nameof(value));

                _predicate = value;
            }
        }

        protected override Expression VisitChildren(ExpressionVisitor visitor)
        {
            visitor.Visit(_tableExpression);
            visitor.Visit(_predicate);

            return this;
        }
    }
}
