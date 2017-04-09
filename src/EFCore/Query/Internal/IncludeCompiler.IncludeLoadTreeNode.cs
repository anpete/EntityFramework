// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;

namespace Microsoft.EntityFrameworkCore.Query.Internal
{
    public partial class IncludeCompiler
    {
        private sealed class IncludeLoadTreeNode : IncludeLoadTreeNodeBase
        {
            private static readonly MethodInfo _referenceEqualsMethodInfo
                = typeof(object).GetTypeInfo()
                    .GetDeclaredMethod(nameof(ReferenceEquals));

            private static readonly MethodInfo _collectionAccessorAddMethodInfo
                = typeof(IClrCollectionAccessor).GetTypeInfo()
                    .GetDeclaredMethod(nameof(IClrCollectionAccessor.Add));

            private static readonly MethodInfo _queryBufferIncludeCollectionMethodInfo
                = typeof(IQueryBuffer).GetTypeInfo()
                    .GetDeclaredMethod(nameof(IQueryBuffer.IncludeCollection));

            private static readonly MethodInfo _queryBufferIncludeCollectionAsyncMethodInfo
                = typeof(IQueryBuffer).GetTypeInfo()
                    .GetDeclaredMethod(nameof(IQueryBuffer.IncludeCollectionAsync));

            public IncludeLoadTreeNode(INavigation navigation) => Navigation = navigation;

            public INavigation Navigation { get; }

            public Expression Compile(
                Expression targetExpression,
                Expression entityParameter,
                ICollection<Expression> propertyExpressions,
                bool trackingQuery,
                bool asyncQuery,
                ref int includedIndex,
                ref int collectionIncludeId)
            {
                if (Navigation.IsCollection())
                {
                    var mainFromClause 
                        = new MainFromClause(
                            "a",
                            Navigation.GetTargetType().ClrType,
                            Expression.Property(
                                targetExpression,
                                Navigation.PropertyInfo));

                    var collectionQueryModel
                        = new QueryModel(
                            mainFromClause, 
                            new SelectClause(new QuerySourceReferenceExpression(mainFromClause)));

                    //                    Expression collectionLambdaExpression
                    //                        = Expression.Lambda<Func<IEnumerable<object>>>(
                    //                            Expression.Property(
                    //                                targetExpression,
                    //                                Navigation.PropertyInfo));

                    Expression collectionLambdaExpression
                        = Expression.Lambda<Func<IEnumerable<object>>>(
                            new SubQueryExpression(collectionQueryModel));

                    var includeCollectionMethodInfo = _queryBufferIncludeCollectionMethodInfo;

                    Expression cancellationTokenExpression = null;

                    if (asyncQuery)
                    {
                        collectionLambdaExpression
                            = Expression.Convert(
                                collectionLambdaExpression,
                                typeof(Func<IAsyncEnumerable<object>>));

                        includeCollectionMethodInfo = _queryBufferIncludeCollectionAsyncMethodInfo;
                        cancellationTokenExpression = _cancellationTokenParameter;
                    }

                    return
                        BuildCollectionIncludeExpressions(
                            Navigation,
                            entityParameter,
                            trackingQuery,
                            collectionLambdaExpression,
                            includeCollectionMethodInfo,
                            cancellationTokenExpression,
                            ref collectionIncludeId);
                }

                return
                    Compile(
                        propertyExpressions,
                        entityParameter,
                        trackingQuery,
                        asyncQuery,
                        ref includedIndex,
                        ref collectionIncludeId,
                        targetExpression);
            }

            private Expression Compile(
                ICollection<Expression> propertyExpressions,
                Expression targetEntityExpression,
                bool trackingQuery,
                bool asyncQuery,
                ref int includedIndex,
                ref int collectionIncludeId,
                Expression lastPropertyExpression)
            {
                propertyExpressions.Add(
                    lastPropertyExpression
                        = EntityQueryModelVisitor.CreatePropertyExpression(
                            lastPropertyExpression, Navigation));

                var relatedArrayAccessExpression
                    = Expression.ArrayAccess(_includedParameter, Expression.Constant(includedIndex++));

                var relatedEntityExpression
                    = Expression.Convert(relatedArrayAccessExpression, Navigation.ClrType);

                var stateManagerProperty
                    = Expression.Property(
                        Expression.Property(
                            EntityQueryModelVisitor.QueryContextParameter,
                            nameof(QueryContext.StateManager)),
                        nameof(Lazy<object>.Value));

                var blockExpressions = new List<Expression>();

                if (trackingQuery)
                {
                    blockExpressions.Add(
                        Expression.Call(
                            Expression.Property(
                                EntityQueryModelVisitor.QueryContextParameter,
                                nameof(QueryContext.QueryBuffer)),
                            _queryBufferStartTrackingMethodInfo,
                            relatedArrayAccessExpression,
                            Expression.Constant(Navigation.GetTargetType())));

                    blockExpressions.Add(
                        Expression.Call(
                            _setRelationshipSnapshotValueMethodInfo,
                            stateManagerProperty,
                            Expression.Constant(Navigation),
                            targetEntityExpression,
                            relatedArrayAccessExpression));
                }
                else
                {
                    blockExpressions.Add(
                        Expression.Assign(
                            Expression.MakeMemberAccess(
                                targetEntityExpression,
                                Navigation.GetMemberInfo(false, true)),
                            relatedEntityExpression));
                }

                var inverseNavigation = Navigation.FindInverse();

                if (inverseNavigation != null)
                {
                    var collection = inverseNavigation.IsCollection();

                    if (trackingQuery)
                    {
                        blockExpressions.Add(
                            Expression.Call(
                                collection
                                    ? _addToCollectionSnapshotMethodInfo
                                    : _setRelationshipSnapshotValueMethodInfo,
                                stateManagerProperty,
                                Expression.Constant(inverseNavigation),
                                relatedArrayAccessExpression,
                                targetEntityExpression));
                    }
                    else
                    {
                        blockExpressions.Add(
                            collection
                                ? (Expression)Expression.Call(
                                    Expression.Constant(inverseNavigation.GetCollectionAccessor()),
                                    _collectionAccessorAddMethodInfo,
                                    relatedArrayAccessExpression,
                                    targetEntityExpression)
                                : Expression.Assign(
                                    Expression.MakeMemberAccess(
                                        relatedEntityExpression,
                                        inverseNavigation
                                            .GetMemberInfo(forConstruction: false, forSet: true)),
                                    targetEntityExpression));
                    }
                }

                // ReSharper disable once LoopCanBeConvertedToQuery
                foreach (var includeLoadTreeNode in Children)
                {
                    //                    blockExpressions.Add(
                    //                        includeLoadTreeNode.Compile(
                    //                            propertyExpressions,
                    //                            relatedEntityExpression,
                    //                            trackingQuery,
                    //                            ref includedIndex,
                    //                            lastPropertyExpression));

                    blockExpressions.Add(
                        includeLoadTreeNode.Compile(
                            lastPropertyExpression,
                            relatedEntityExpression,
                            propertyExpressions,
                            trackingQuery,
                            asyncQuery,
                            ref includedIndex,
                            ref collectionIncludeId));
                }

                return
                    Expression.IfThen(
                        Expression.Not(
                            Expression.Call(
                                _referenceEqualsMethodInfo,
                                relatedArrayAccessExpression,
                                Expression.Constant(null, typeof(object)))),
                        Expression.Block(typeof(void), blockExpressions));
            }

            private static Expression BuildCollectionIncludeExpressions(
                INavigation navigation,
                Expression targetEntityExpression,
                bool trackingQuery,
                Expression relatedCollectionFuncExpression,
                MethodInfo includeCollectionMethodInfo,
                Expression cancellationTokenExpression,
                ref int collectionIncludeId)
            {
                var inverseNavigation = navigation.FindInverse();

                var arguments = new List<Expression>
                {
                    Expression.Constant(collectionIncludeId++),
                    Expression.Constant(navigation),
                    Expression.Constant(inverseNavigation, typeof(INavigation)),
                    Expression.Constant(navigation.GetTargetType()),
                    Expression.Constant(navigation.GetCollectionAccessor()),
                    Expression.Constant(inverseNavigation?.GetSetter(), typeof(IClrPropertySetter)),
                    Expression.Constant(trackingQuery),
                    targetEntityExpression,
                    relatedCollectionFuncExpression
                };

                if (cancellationTokenExpression != null)
                {
                    arguments.Add(cancellationTokenExpression);
                }

                return Expression.Call(
                    Expression.Property(
                        EntityQueryModelVisitor.QueryContextParameter,
                        nameof(QueryContext.QueryBuffer)),
                    includeCollectionMethodInfo,
                    arguments);
            }

            private static readonly MethodInfo _setRelationshipSnapshotValueMethodInfo
                = typeof(IncludeLoadTreeNode).GetTypeInfo()
                    .GetDeclaredMethod(nameof(SetRelationshipSnapshotValue));

            private static void SetRelationshipSnapshotValue(
                IStateManager stateManager,
                IPropertyBase navigation,
                object entity,
                object value)
            {
                var internalEntityEntry = stateManager.TryGetEntry(entity);

                Debug.Assert(internalEntityEntry != null);

                internalEntityEntry.SetRelationshipSnapshotValue(navigation, value);
            }

            private static readonly MethodInfo _addToCollectionSnapshotMethodInfo
                = typeof(IncludeLoadTreeNode).GetTypeInfo()
                    .GetDeclaredMethod(nameof(AddToCollectionSnapshot));

            private static void AddToCollectionSnapshot(
                IStateManager stateManager,
                IPropertyBase navigation,
                object entity,
                object value)
            {
                var internalEntityEntry = stateManager.TryGetEntry(entity);

                Debug.Assert(internalEntityEntry != null);

                internalEntityEntry.AddToCollectionSnapshot(navigation, value);
            }
        }
    }
}
