// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq.Expressions;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Query.Sql;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore.Query.Expressions
{
    /// <summary>
    ///     An alias expression.
    /// </summary>
    public class AliasExpression : Expression
    {
        private readonly Expression _expression;

        private string _alias;

        private Expression _sourceExpression;

        /// <summary>
        ///     Creates a new instance of an AliasExpression.
        /// </summary>
        /// <param name="expression"> The expression being aliased. </param>
        public AliasExpression([NotNull] Expression expression)
        {
            Check.NotNull(expression, nameof(expression));

            _expression = expression;
        }

        // TODO: Revisit the design here, "alias" should really be required.

        /// <summary>
        ///     Creates a new instance of an AliasExpression.
        /// </summary>
        /// <param name="alias"> The alias. </param>
        /// <param name="expression"> The expression being aliased.  </param>
        public AliasExpression([CanBeNull] string alias, [NotNull] Expression expression)
        {
            Check.NotNull(expression, nameof(expression));

            _alias = alias;
            _expression = expression;
        }

        /// <summary>
        ///     Gets or sets the alias.
        /// </summary>
        /// <value>
        ///     The alias.
        /// </value>
        public virtual string Alias
        {
            get { return _alias; }
            [param: NotNull]
            // TODO: Remove mutability here
            set
            {
                Check.NotNull(value, nameof(value));

                _alias = value;
            }
        }

        /// <summary>
        ///     The expression being aliased.
        /// </summary>
        public virtual Expression Expression => _expression;

        // TODO: Revisit why we need this. Try and remove

        /// <summary>
        ///     Gets or sets a value indicating whether the expression is being projected.
        /// </summary>
        /// <value>
        ///     true if projected, false if not.
        /// </value>
        public virtual bool IsProjected { get; set; } = false;

        public override ExpressionType NodeType => ExpressionType.Extension;
        public override Type Type => _expression.Type;

        /// <summary>
        ///     Gets or sets the source expression.
        /// </summary>
        /// <value>
        ///     The source expression.
        /// </value>
        public virtual Expression SourceExpression
        {
            get { return _sourceExpression; }
            [param: NotNull]
            set
            {
                Check.NotNull(value, nameof(value));

                _sourceExpression = value;
            }
        }

        /// <summary>
        ///     Gets or sets the source member.
        /// </summary>
        /// <value>
        ///     The source member.
        /// </value>
        public virtual MemberInfo SourceMember { get; [param: CanBeNull] set; }

        protected override Expression Accept(ExpressionVisitor visitor)
        {
            Check.NotNull(visitor, nameof(visitor));

            var specificVisitor = visitor as ISqlExpressionVisitor;

            return specificVisitor != null
                ? specificVisitor.VisitAlias(this)
                : base.Accept(visitor);
        }

        protected override Expression VisitChildren(ExpressionVisitor visitor)
        {
            var newInnerExpression = visitor.Visit(_expression);

            return newInnerExpression != _expression
                ? new AliasExpression(Alias, newInnerExpression)
                : this;
        }

        public override string ToString()
            => Alias != null ? "(" + _expression + ") AS " + Alias : _expression.ToString();

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            return obj.GetType() == GetType()
                   && Equals((AliasExpression)obj);
        }

        private bool Equals(AliasExpression other)
            => Equals(_expression, other._expression)
               && string.Equals(_alias, other._alias);

        public override int GetHashCode()
        {
            unchecked
            {
                // ReSharper disable once NonReadonlyMemberInGetHashCode
                return (_expression.GetHashCode() * 397) ^ (_alias?.GetHashCode() ?? 0);
            }
        }
    }
}
