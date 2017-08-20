// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using JetBrains.Annotations;

namespace Microsoft.EntityFrameworkCore.Metadata
{
    /// <summary>
    ///     <para>
    ///         Represents a structural type in an <see cref="IMutableModel" />.
    ///     </para>
    ///     <para>
    ///         This interface is used during model creation and allows the metadata to be modified.
    ///         Once the model is built, <see cref="IEntityType" /> represents a ready-only view of the same metadata.
    ///     </para>
    /// </summary>
    public interface IMutableStructuralType : IStructuralType, IMutableTypeBase
    {
        /// <summary>
        ///     Gets or sets the base type of the structural type. Returns null if this is not a derived type in an inheritance hierarchy.
        /// </summary>
        new IMutableStructuralType BaseType { get; [param: CanBeNull] set; }

        /// <summary>
        ///     Gets the LINQ expression filter automatically applied to queries for this structural type.
        /// </summary>
        new LambdaExpression QueryFilter { get; [param: CanBeNull] set; }

        /// <summary>
        ///     Adds a new alternate key to this structural type.
        /// </summary>
        /// <param name="properties"> The properties that make up the alternate key. </param>
        /// <returns> The newly created key. </returns>
        IMutableKey AddKey([NotNull] IReadOnlyList<IMutableProperty> properties);

        /// <summary>
        ///     Gets the primary or alternate key that is defined on the given properties. Returns null if no key is defined
        ///     for the given properties.
        /// </summary>
        /// <param name="properties"> The properties that make up the key. </param>
        /// <returns> The key, or null if none is defined. </returns>
        new IMutableKey FindKey([NotNull] IReadOnlyList<IProperty> properties);

        /// <summary>
        ///     Gets the primary and alternate keys for this structural type.
        /// </summary>
        /// <returns> The primary and alternate keys. </returns>
        new IEnumerable<IMutableKey> GetKeys();

        /// <summary>
        ///     Removes a primary or alternate key from this structural type.
        /// </summary>
        /// <param name="properties"> The properties that make up the key. </param>
        /// <returns> The key that was removed. </returns>
        IMutableKey RemoveKey([NotNull] IReadOnlyList<IProperty> properties);

        /// <summary>
        ///     Adds a new relationship to this structural type.
        /// </summary>
        /// <param name="properties"> The properties that the foreign key is defined on. </param>
        /// <param name="principalKey"> The primary or alternate key that is referenced. </param>
        /// <param name="principalEntityType">
        ///     The structural type that the relationship targets. This may be different from the type that <paramref name="principalKey" />
        ///     is defined on when the relationship targets a derived type in an inheritance hierarchy (since the key is defined on the
        ///     base type of the hierarchy).
        /// </param>
        /// <returns> The newly created foreign key. </returns>
        IMutableForeignKey AddForeignKey(
            [NotNull] IReadOnlyList<IMutableProperty> properties,
            [NotNull] IMutableKey principalKey,
            [NotNull] IMutableEntityType principalEntityType);

        /// <summary>
        ///     Gets the foreign key for the given properties that points to a given primary or alternate key. Returns null
        ///     if no foreign key is found.
        /// </summary>
        /// <param name="properties"> The properties that the foreign key is defined on. </param>
        /// <param name="principalKey"> The primary or alternate key that is referenced. </param>
        /// <param name="principalEntityType">
        ///     The structural type that the relationship targets. This may be different from the type that <paramref name="principalKey" />
        ///     is defined on when the relationship targets a derived type in an inheritance hierarchy (since the key is defined on the
        ///     base type of the hierarchy).
        /// </param>
        /// <returns> The foreign key, or null if none is defined. </returns>
        new IMutableForeignKey FindForeignKey(
            [NotNull] IReadOnlyList<IProperty> properties,
            [NotNull] IKey principalKey,
            [NotNull] IEntityType principalEntityType);

        /// <summary>
        ///     Gets the foreign keys defined on this structural type.
        /// </summary>
        /// <returns> The foreign keys defined on this structural type. </returns>
        new IEnumerable<IMutableForeignKey> GetForeignKeys();

        /// <summary>
        ///     Removes a relationship from this structural type.
        /// </summary>
        /// <param name="properties"> The properties that the foreign key is defined on. </param>
        /// <param name="principalKey"> The primary or alternate key that is referenced. </param>
        /// <param name="principalEntityType">
        ///     The structural type that the relationship targets. This may be different from the type that <paramref name="principalKey" />
        ///     is defined on when the relationship targets a derived type in an inheritance hierarchy (since the key is defined on the
        ///     base type of the hierarchy).
        /// </param>
        /// <returns> The foreign key that was removed. </returns>
        IMutableForeignKey RemoveForeignKey(
            [NotNull] IReadOnlyList<IProperty> properties,
            [NotNull] IKey principalKey,
            [NotNull] IEntityType principalEntityType);

        /// <summary>
        ///     Adds an index to this structural type.
        /// </summary>
        /// <param name="properties"> The properties that are to be indexed. </param>
        /// <returns> The newly created index. </returns>
        IMutableIndex AddIndex([NotNull] IReadOnlyList<IMutableProperty> properties);

        /// <summary>
        ///     Gets the index defined on the given properties. Returns null if no index is defined.
        /// </summary>
        /// <param name="properties"> The properties to find the index on. </param>
        /// <returns> The index, or null if none is found. </returns>
        new IMutableIndex FindIndex([NotNull] IReadOnlyList<IProperty> properties);

        /// <summary>
        ///     Gets the indexes defined on this structural type.
        /// </summary>
        /// <returns> The indexes defined on this structural type. </returns>
        new IEnumerable<IMutableIndex> GetIndexes();

        /// <summary>
        ///     Removes an index from this structural type.
        /// </summary>
        /// <param name="properties"> The properties that make up the index. </param>
        /// <returns> The index that was removed. </returns>
        IMutableIndex RemoveIndex([NotNull] IReadOnlyList<IProperty> properties);

        /// <summary>
        ///     Adds a property to this structural type.
        /// </summary>
        /// <param name="name"> The name of the property to add. </param>
        /// <param name="propertyType"> The type of value the property will hold. </param>
        /// <returns> The newly created property. </returns>
        IMutableProperty AddProperty([NotNull] string name, [CanBeNull] Type propertyType);

        /// <summary>
        ///     <para>
        ///         Gets the property with a given name. Returns null if no property with the given name is defined.
        ///     </para>
        ///     <para>
        ///         This API only finds scalar properties and does not find navigation properties. Use
        ///         <see cref="MutableEntityTypeExtensions.FindNavigation(IMutableEntityType, string)" /> to find
        ///         a navigation property.
        ///     </para>
        /// </summary>
        /// <param name="name"> The name of the property. </param>
        /// <returns> The property, or null if none is found. </returns>
        new IMutableProperty FindProperty([NotNull] string name);

        /// <summary>
        ///     <para>
        ///         Gets the properties defined on this structural type.
        ///     </para>
        ///     <para>
        ///         This API only returns scalar properties and does not return navigation properties. Use
        ///         <see cref="MutableEntityTypeExtensions.GetNavigations(IMutableEntityType)" /> to get navigation
        ///         properties.
        ///     </para>
        /// </summary>
        /// <returns> The properties defined on this structural type. </returns>
        new IEnumerable<IMutableProperty> GetProperties();

        /// <summary>
        ///     Removes a property from this structural type.
        /// </summary>
        /// <param name="name"> The name of the property to remove. </param>
        /// <returns> The property that was removed. </returns>
        IMutableProperty RemoveProperty([NotNull] string name);
    }
}
