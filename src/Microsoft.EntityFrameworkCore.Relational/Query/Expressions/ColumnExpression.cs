// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq.Expressions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query.Sql;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore.Query.Expressions
{
    /// <summary>
    ///     A column expression.
    /// </summary>
    public class ColumnExpression : Expression
    {
        private readonly IProperty _property;
        private readonly TableExpressionBase _tableExpression;

        /// <summary>
        ///     Creates a new instance of a ColumnExpression.
        /// </summary>
        /// <param name="name"> The column name. </param>
        /// <param name="property"> The corresponding property. </param>
        /// <param name="tableExpression"> The target table expression. </param>
        public ColumnExpression(
            [NotNull] string name,
            [NotNull] IProperty property,
            [NotNull] TableExpressionBase tableExpression)
            : this(name, Check.NotNull(property, nameof(property)).ClrType, tableExpression)
        {
            _property = property;
        }

        /// <summary>
        ///     Creates a new instance of a ColumnExpression.
        /// </summary>
        /// <param name="name"> The column name. </param>
        /// <param name="type"> The column type. </param>
        /// <param name="tableExpression"> The target table expression. </param>
        public ColumnExpression(
            [NotNull] string name,
            [NotNull] Type type,
            [NotNull] TableExpressionBase tableExpression)
        {
            Check.NotEmpty(name, nameof(name));
            Check.NotNull(type, nameof(type));
            Check.NotNull(tableExpression, nameof(tableExpression));

            Name = name;
            Type = type;
            _tableExpression = tableExpression;
        }

        /// <summary>
        ///     The target table.
        /// </summary>
        public virtual TableExpressionBase Table => _tableExpression;

        /// <summary>
        ///     The target table alias.
        /// </summary>
        public virtual string TableAlias => _tableExpression.Alias;

#pragma warning disable 108

        /// <summary>
        ///     The corresponding property.
        /// </summary>
        public virtual IProperty Property => _property;
#pragma warning restore 108

        /// <summary>
        ///     Gets the column name.
        /// </summary>
        /// <value>
        ///     The column name.
        /// </value>
        public virtual string Name { get; }

        public override ExpressionType NodeType => ExpressionType.Extension;

        public override Type Type { get; }

        protected override Expression Accept(ExpressionVisitor visitor)
        {
            Check.NotNull(visitor, nameof(visitor));

            var specificVisitor = visitor as ISqlExpressionVisitor;

            return specificVisitor != null
                ? specificVisitor.VisitColumn(this)
                : base.Accept(visitor);
        }

        protected override Expression VisitChildren(ExpressionVisitor visitor) => this;

        private bool Equals([NotNull] ColumnExpression other)
            => ((_property == null && other._property == null)
                || (_property != null && _property.Equals(other._property)))
               && Type == other.Type
               && _tableExpression.Equals(other._tableExpression);

        public override bool Equals([CanBeNull] object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            return (obj.GetType() == GetType())
                   && Equals((ColumnExpression)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (_property.GetHashCode() * 397)
                       ^ _tableExpression.GetHashCode();
            }
        }

        public override string ToString() => _tableExpression.Alias + "." + Name;
    }
}
