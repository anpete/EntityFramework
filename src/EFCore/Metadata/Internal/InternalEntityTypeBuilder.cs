// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Internal;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore.Metadata.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    [DebuggerDisplay("{Metadata,nq}")]
    public class InternalEntityTypeBuilder : InternalStructuralTypeBuilder<EntityType>
    {
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public InternalEntityTypeBuilder([NotNull] EntityType metadata, [NotNull] InternalModelBuilder modelBuilder)
            : base(metadata, modelBuilder)
        {
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual InternalKeyBuilder PrimaryKey([CanBeNull] IReadOnlyList<string> propertyNames, ConfigurationSource configurationSource)
            => PrimaryKey(GetOrCreateProperties(propertyNames, configurationSource), configurationSource);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual InternalKeyBuilder PrimaryKey([CanBeNull] IReadOnlyList<PropertyInfo> clrProperties, ConfigurationSource configurationSource)
            => PrimaryKey(GetOrCreateProperties(clrProperties, configurationSource), configurationSource);

        private InternalKeyBuilder PrimaryKey(IReadOnlyList<Property> properties, ConfigurationSource configurationSource)
        {
            var previousPrimaryKey = Metadata.FindPrimaryKey();
            if (properties == null)
            {
                if (previousPrimaryKey == null)
                {
                    return null;
                }
            }
            else if (previousPrimaryKey != null
                     && PropertyListComparer.Instance.Compare(previousPrimaryKey.Properties, properties) == 0)
            {
                return Metadata.SetPrimaryKey(properties, configurationSource).Builder;
            }

            var primaryKeyConfigurationSource = Metadata.GetPrimaryKeyConfigurationSource();
            if (primaryKeyConfigurationSource.HasValue
                && !configurationSource.Overrides(primaryKeyConfigurationSource.Value))
            {
                return null;
            }

            InternalKeyBuilder keyBuilder = null;
            if (properties == null)
            {
                Metadata.SetPrimaryKey(properties, configurationSource);
            }
            else
            {
                using (ModelBuilder.Metadata.ConventionDispatcher.StartBatch())
                {
                    keyBuilder = HasKeyInternal(properties, configurationSource);
                    if (keyBuilder == null)
                    {
                        return null;
                    }

                    Metadata.SetPrimaryKey(keyBuilder.Metadata.Properties, configurationSource);
                    foreach (var key in Metadata.GetDeclaredKeys().ToList())
                    {
                        if (key == keyBuilder.Metadata)
                        {
                            continue;
                        }

                        var referencingForeignKeys = key
                            .GetReferencingForeignKeys()
                            .Where(fk => fk.GetPrincipalKeyConfigurationSource() == null)
                            .ToList();

                        foreach (var referencingForeignKey in referencingForeignKeys)
                        {
                            DetachRelationship(referencingForeignKey).Attach();
                        }
                    }
                }
            }

            if (previousPrimaryKey?.Builder != null)
            {
                RemoveKeyIfUnused(previousPrimaryKey);
            }

            return keyBuilder?.Metadata.Builder == null ? null : keyBuilder;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override void RemoveKeyIfUnused(Key key)
        {
            if (Metadata.FindPrimaryKey() == key)
            {
                return;
            }

            base.RemoveKeyIfUnused(key);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual InternalRelationshipBuilder Owns(
            [NotNull] string targetEntityTypeName,
            [NotNull] string navigationName,
            ConfigurationSource configurationSource)
            => Owns(
                new TypeIdentity(targetEntityTypeName), PropertyIdentity.Create(navigationName),
                inverse: null, configurationSource: configurationSource);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual InternalRelationshipBuilder Owns(
            [NotNull] string targetEntityTypeName,
            [NotNull] PropertyInfo navigationProperty,
            ConfigurationSource configurationSource)
            => Owns(
                new TypeIdentity(targetEntityTypeName), PropertyIdentity.Create(navigationProperty),
                inverse: null, configurationSource: configurationSource);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual InternalRelationshipBuilder Owns(
            [NotNull] Type targetEntityType,
            [NotNull] string navigationName,
            ConfigurationSource configurationSource)
            => Owns(
                new TypeIdentity(targetEntityType), PropertyIdentity.Create(navigationName),
                inverse: null, configurationSource: configurationSource);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual InternalRelationshipBuilder Owns(
            [NotNull] Type targetEntityType,
            [NotNull] PropertyInfo navigationProperty,
            ConfigurationSource configurationSource)
            => Owns(
                new TypeIdentity(targetEntityType), PropertyIdentity.Create(navigationProperty),
                inverse: null, configurationSource: configurationSource);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual InternalRelationshipBuilder Owns(
            [NotNull] Type targetEntityType,
            [NotNull] PropertyInfo navigationProperty,
            [CanBeNull] PropertyInfo inverseProperty,
            ConfigurationSource configurationSource)
            => Owns(
                new TypeIdentity(targetEntityType),
                PropertyIdentity.Create(navigationProperty),
                PropertyIdentity.Create(inverseProperty),
                configurationSource);

        private InternalRelationshipBuilder Owns(
            TypeIdentity targetEntityType,
            PropertyIdentity navigation,
            PropertyIdentity? inverse,
            ConfigurationSource configurationSource)
        {
            InternalEntityTypeBuilder ownedEntityType;
            InternalRelationshipBuilder relationship;
            using (var batch = Metadata.Model.ConventionDispatcher.StartBatch())
            {
                var existingNavigation = Metadata
                    .FindNavigationsInHierarchy(navigation.Name)
                    .SingleOrDefault(n => n.GetTargetType().Name == targetEntityType.Name && n.GetTargetType().HasDefiningNavigation());

                var builder = existingNavigation?.ForeignKey.Builder;

                if (builder != null)
                {
                    builder = builder.RelatedEntityTypes(Metadata, existingNavigation.GetTargetType(), configurationSource);
                    builder = builder?.IsRequired(true, configurationSource);
                    builder = builder?.IsOwnership(true, configurationSource);
                    builder = builder?.Navigations(inverse, navigation, configurationSource);

                    return builder == null ? null : batch.Run(builder);
                }

                ownedEntityType = targetEntityType.Type == null
                    ? ModelBuilder.Entity(targetEntityType.Name, navigation.Name, Metadata, configurationSource)
                    : ModelBuilder.Entity(targetEntityType.Type, navigation.Name, Metadata, configurationSource);

                if (ownedEntityType == null)
                {
                    return null;
                }

                relationship = ownedEntityType.Relationship(
                    targetEntityTypeBuilder: this,
                    navigationToTarget: inverse,
                    inverseNavigation: navigation,
                    setTargetAsPrincipal: true,
                    configurationSource: configurationSource,
                    required: true);
                relationship = batch.Run(relationship.IsOwnership(true, configurationSource));
            }

            if (relationship?.Metadata.Builder == null)
            {
                if (ownedEntityType.Metadata.Builder != null)
                {
                    ModelBuilder.RemoveEntityType(ownedEntityType.Metadata, configurationSource);
                }
                return null;
            }

            return relationship;
        }
    }
}
