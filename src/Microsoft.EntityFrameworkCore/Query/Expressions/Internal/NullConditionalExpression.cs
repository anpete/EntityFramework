// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq.Expressions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Query.ExpressionVisitors;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore.Query.Expressions.Internal
{
    /// <summary>
    ///     Expression representing null-conditional access.
    /// </summary>
    public class NullConditionalExpression : Expression
    {
        private readonly Type _type;

        /// <summary>
        ///     Creates a new instance of NullConditionalExpression.
        /// </summary>
        /// <param name="nullableCaller"> Expression representing potentially nullable caller that needs to be tested for it's nullability. </param>
        /// <param name="caller"> Expression representing actual caller for the access operation. </param>
        /// <param name="accessOperation"> Expression representing access operation. </param>
        public NullConditionalExpression(
            [NotNull] Expression nullableCaller,
            [NotNull] Expression caller,
            [NotNull] Expression accessOperation)
        {
            Check.NotNull(nullableCaller, nameof(nullableCaller));
            Check.NotNull(caller, nameof(caller));
            Check.NotNull(accessOperation, nameof(accessOperation));

            NullableCaller = nullableCaller;
            Caller = caller;
            AccessOperation = accessOperation;

            _type = accessOperation.Type.IsNullableType()
                ? accessOperation.Type
                : accessOperation.Type.MakeNullable();
        }

        /// <summary>
        ///     Expression representing potentially nullable caller that needs to be tested for it's nullability.
        /// </summary>
        public virtual Expression NullableCaller { get; }

        /// <summary>
        ///     Expression representing actual caller for the access operation.
        /// </summary>
        public virtual Expression Caller { get; }

        /// <summary>
        ///     Expression representing access operation.
        /// </summary>
        public virtual Expression AccessOperation { get; }

        /// <summary>
        ///     Indicates that the node can be reduced to a simpler node. If this returns true,
        ///     Reduce() can be called to produce the reduced form.
        /// </summary>
        public override bool CanReduce => true;

        /// <summary>
        ///     Gets the static type of the expression that this expression represents.
        /// </summary>
        public override Type Type => _type;

        /// <summary>
        ///     Gets the node type of this expression.
        /// </summary>
        public override ExpressionType NodeType => ExpressionType.Extension;

        /// <summary>
        ///     Reduces this node to a simpler expression. If CanReduce returns true, this should
        ///     return a valid expression. This method can return another node which itself must
        ///     be reduced.
        /// </summary>
        public override Expression Reduce()
        {
            var nullableCallerType = NullableCaller.Type;
            var nullableCaller = Parameter(nullableCallerType, "__caller");
            var result = Parameter(_type, "__result");

            var caller = Caller.Type != nullableCaller.Type
                ? (Expression)Convert(nullableCaller, Caller.Type)
                : nullableCaller;

            var operation = new CallerReplacingExpressionVisitor(Caller, caller).Visit(AccessOperation);

            if (operation.Type != _type)
            {
                operation = Convert(operation, _type);
            }

            var resultExpression =
                Block(
                    new[] { nullableCaller, result },
                    Assign(nullableCaller, NullableCaller),
                    Assign(result, Default(_type)),
                    IfThen(
                        NotEqual(nullableCaller, Default(nullableCallerType)),
                        Assign(result, operation)),
                    result);

            return resultExpression;
        }

        private class CallerReplacingExpressionVisitor : ExpressionVisitorBase
        {
            private readonly Expression _originalCaller;
            private readonly Expression _newCaller;

            public CallerReplacingExpressionVisitor(Expression originalCaller, Expression newCaller)
            {
                _originalCaller = originalCaller;
                _newCaller = newCaller;
            }

            public override Expression Visit(Expression node)
                => node == _originalCaller
                    ? _newCaller
                    : base.Visit(node);
        }
    }
}
