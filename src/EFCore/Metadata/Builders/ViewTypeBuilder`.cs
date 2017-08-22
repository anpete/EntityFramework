// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Internal;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore.Metadata.Builders
{
    /// <summary>
    ///     <para>
    ///         Provides a simple API for configuring a view <see cref="EntityType" />.
    ///     </para>
    ///     <para>
    ///         Instances of this class are returned from methods when using the <see cref="ModelBuilder" /> API
    ///         and it is not designed to be directly constructed in your application code.
    ///     </para>
    /// </summary>
    /// <typeparam name="TView"> The entity type being configured. </typeparam>
    public class ViewTypeBuilder<TView>
        where TView : class
    {
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public ViewTypeBuilder(InternalEntityTypeBuilder entityTypeBuilder)
        {
            Builder = entityTypeBuilder;
        }

        /// <summary>
        ///     Creates an alternate key in the model for this entity type if one does not already exist over the specified
        ///     properties. This will force the properties to be read-only. Use <see cref="HasIndex" /> to specify uniqueness
        ///     in the model that does not force properties to be read-only.
        /// </summary>
        /// <param name="keyExpression">
        ///     <para>
        ///         A lambda expression representing the key property(s) (<c>blog => blog.Url</c>).
        ///     </para>
        ///     <para>
        ///         If the key is made up of multiple properties then specify an anonymous type including
        ///         the properties (<c>post => new { post.Title, post.BlogId }</c>).
        ///     </para>
        /// </param>
        /// <returns> An object that can be used to configure the key. </returns>
        public virtual KeyBuilder HasAlternateKey([NotNull] Expression<Func<TView, object>> keyExpression)
            => new KeyBuilder(
                Builder.HasKey(
                    Check.NotNull(keyExpression, nameof(keyExpression)).GetPropertyAccessList(), ConfigurationSource.Explicit));

        /// <summary>
        ///     Configures an index on the specified properties. If there is an existing index on the given
        ///     set of properties, then the existing index will be returned for configuration.
        /// </summary>
        /// <param name="indexExpression">
        ///     <para>
        ///         A lambda expression representing the property(s) to be included in the index
        ///         (<c>blog => blog.Url</c>).
        ///     </para>
        ///     <para>
        ///         If the index is made up of multiple properties then specify an anonymous type including the
        ///         properties (<c>post => new { post.Title, post.BlogId }</c>).
        ///     </para>
        /// </param>
        /// <returns> An object that can be used to configure the index. </returns>
        public virtual IndexBuilder HasIndex([NotNull] Expression<Func<TView, object>> indexExpression)
            => new IndexBuilder(
                Builder.HasIndex(
                    Check.NotNull(indexExpression, nameof(indexExpression)).GetPropertyAccessList(), ConfigurationSource.Explicit));

//        /// <summary>
//        ///     <para>
//        ///         Configures a relationship where this view type has a collection that contains
//        ///         instances of the other type in the relationship.
//        ///     </para>
//        ///     <para>
//        ///         After calling this method, you should chain a call to
//        ///         <see
//        ///             cref="CollectionNavigationBuilder{TView,TRelatedView}.WithOne(Expression{Func{TRelatedView,TView}})" />
//        ///         to fully configure the relationship. Calling just this method without the chained call will not
//        ///         produce a valid relationship.
//        ///     </para>
//        /// </summary>
//        /// <typeparam name="TRelatedView"> The view type that this relationship targets. </typeparam>
//        /// <param name="navigationExpression">
//        ///     A lambda expression representing the collection navigation property on this view type that represents
//        ///     the relationship (<c>blog => blog.Posts</c>). If no property is specified, the relationship will be
//        ///     configured without a navigation property on this end.
//        /// </param>
//        /// <returns> An object that can be used to configure the relationship. </returns>
//        public virtual object HasMany<TRelatedView>(
//            [CanBeNull] Expression<Func<TView, IEnumerable<TRelatedView>>> navigationExpression = null)
//            where TRelatedView : class
//        {
//            var relatedViewType = Builder.ModelBuilder.Entity(typeof(TRelatedView), ConfigurationSource.Explicit).Metadata;
//            var navigation = navigationExpression?.GetPropertyAccess();
//
//            InternalRelationshipBuilder relationship;
//
//            using (var batch = Builder.Metadata.Model.ConventionDispatcher.StartBatch())
//            {
//                relationship = relatedViewType.Builder
//                    .Relationship(Builder, ConfigurationSource.Explicit)
//                    .IsUnique(false, ConfigurationSource.Explicit)
//                    .RelatedEntityTypes(Builder.Metadata, relatedViewType, ConfigurationSource.Explicit)
//                    .PrincipalToDependent(navigation, ConfigurationSource.Explicit);
//
//                relationship = batch.Run(relationship);
//            }
//
//            //            return new CollectionNavigationBuilder<TView, TRelatedView>(
//            //                Builder.Metadata,
//            //                relatedViewType,
//            //                navigation,
//            //                relationship);
//
//            return null;
//        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public InternalEntityTypeBuilder Builder { get; }
    }
}
