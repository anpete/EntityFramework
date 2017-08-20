// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore.Metadata.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public abstract class StructuralType : TypeBase, IMutableStructuralType
    {
        private readonly SortedSet<ForeignKey> _foreignKeys
            = new SortedSet<ForeignKey>(ForeignKeyComparer.Instance);

        private readonly SortedDictionary<string, Navigation> _navigations
            = new SortedDictionary<string, Navigation>(StringComparer.Ordinal);

        private readonly SortedDictionary<IReadOnlyList<IProperty>, Index> _indexes
            = new SortedDictionary<IReadOnlyList<IProperty>, Index>(PropertyListComparer.Instance);

        private readonly SortedDictionary<string, Property> _properties;

        private readonly SortedDictionary<IReadOnlyList<IProperty>, Key> _keys
            = new SortedDictionary<IReadOnlyList<IProperty>, Key>(PropertyListComparer.Instance);

        private readonly SortedSet<StructuralType> _directlyDerivedTypes;

        private StructuralType _baseType;
        private LambdaExpression _queryFilter;

        private ConfigurationSource? _baseTypeConfigurationSource;

        // Warning: Never access these fields directly as access needs to be thread-safe
        private PropertyCounts _counts;

        private Func<InternalEntityEntry, ISnapshot> _relationshipSnapshotFactory;
        private Func<InternalEntityEntry, ISnapshot> _originalValuesFactory;
        private Func<ValueBuffer, ISnapshot> _shadowValuesFactory;
        private Func<ISnapshot> _emptyShadowValuesFactory;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected StructuralType([NotNull] string name, [NotNull] Model model, ConfigurationSource configurationSource)
            : base(name, model, configurationSource)
        {
            _properties = new SortedDictionary<string, Property>(CreatePropertyComparer());
            _directlyDerivedTypes = new SortedSet<StructuralType>(CreatePathComparer());
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected StructuralType([NotNull] Type clrType, [NotNull] Model model, ConfigurationSource configurationSource)
            : base(clrType, model, configurationSource)
        {
            Check.ValidEntityType(clrType, nameof(clrType));

            _properties = new SortedDictionary<string, Property>(CreatePropertyComparer());
            _directlyDerivedTypes = new SortedSet<StructuralType>(CreatePathComparer());
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected virtual IComparer<string> CreatePropertyComparer()
        {
            return StringComparer.Ordinal;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected virtual IComparer<StructuralType> CreatePathComparer()
        {
            return StructuralTypePathComparer.Instance;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual InternalEntityTypeBuilder Builder { [DebuggerStepThrough] get; [DebuggerStepThrough] [param: CanBeNull] set; }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected virtual SortedDictionary<string, Property> Properties => _properties;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual StructuralType BaseType => _baseType;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual LambdaExpression QueryFilter
        {
            get => _queryFilter;
            [param: CanBeNull]
            set
            {
                if (value != null
                    && (value.Parameters.Count != 1
                        || value.Parameters[0].Type != ClrType
                        || value.ReturnType != typeof(bool)))
                {
                    throw new InvalidOperationException(
                        CoreStrings.BadFilterExpression(value, this.DisplayName(), ClrType));
                }

                _queryFilter = value;
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual void HasBaseType(
            [CanBeNull] StructuralType structuralType,
            ConfigurationSource configurationSource = ConfigurationSource.Explicit)
        {
            if (_baseType == structuralType)
            {
                UpdateBaseTypeConfigurationSource(configurationSource);
                structuralType?.UpdateConfigurationSource(configurationSource);
                return;
            }

            ValidateBaseTypeAllowed();

            // TODO: validate base type is same metadata type

            var originalBaseType = _baseType;

            _baseType?._directlyDerivedTypes.Remove(this);
            _baseType = null;

            if (structuralType != null)
            {
                if (this.HasClrType())
                {
                    ValidateBaseTypeWhenHasClrType(structuralType);
                }

                if (!this.HasClrType()
                    && structuralType.HasClrType())
                {
                    throw new InvalidOperationException(CoreStrings.NonShadowBaseType(this.DisplayName(), structuralType.DisplayName()));
                }

                if (structuralType.InheritsFrom(this))
                {
                    throw new InvalidOperationException(CoreStrings.CircularInheritance(this.DisplayName(), structuralType.DisplayName()));
                }

                if (_keys.Any())
                {
                    throw new InvalidOperationException(CoreStrings.DerivedEntityCannotHaveKeys(this.DisplayName()));
                }

                var propertyCollisions = structuralType.GetProperties()
                    .Select(p => p.Name)
                    .SelectMany(FindDerivedPropertiesInclusive)
                    .ToList();

                if (propertyCollisions.Any())
                {
                    var derivedProperty = propertyCollisions.First();
                    var baseProperty = structuralType.FindProperty(derivedProperty.Name);
                    throw new InvalidOperationException(
                        CoreStrings.DuplicatePropertiesOnBase(
                            this.DisplayName(),
                            structuralType.DisplayName(),
                            derivedProperty.DeclaringEntityType.DisplayName(),
                            derivedProperty.Name,
                            baseProperty.DeclaringEntityType.DisplayName(),
                            baseProperty.Name));
                }

                var navigationCollisions = structuralType.GetNavigations()
                    .Select(p => p.Name)
                    .SelectMany(FindNavigationsInHierarchy)
                    .ToList();
                if (navigationCollisions.Any())
                {
                    throw new InvalidOperationException(
                        CoreStrings.DuplicateNavigationsOnBase(
                            this.DisplayName(),
                            structuralType.DisplayName(),
                            string.Join(", ", navigationCollisions.Select(p => p.Name))));
                }

                _baseType = structuralType;
                _baseType._directlyDerivedTypes.Add(this);
            }

            PropertyMetadataChanged();
            UpdateBaseTypeConfigurationSource(configurationSource);
            structuralType?.UpdateConfigurationSource(configurationSource);

            OnBaseTypeChanged(originalBaseType);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected virtual void ValidateBaseTypeWhenHasClrType([NotNull] StructuralType structuralType)
        {
            if (!structuralType.HasClrType())
            {
                throw new InvalidOperationException(
                    CoreStrings.NonClrBaseType(this.DisplayName(), structuralType.DisplayName()));
            }

            if (!structuralType.ClrType.GetTypeInfo().IsAssignableFrom(ClrType.GetTypeInfo()))
            {
                throw new InvalidOperationException(
                    CoreStrings.NotAssignableClrBaseType(
                        this.DisplayName(), 
                        structuralType.DisplayName(), 
                        ClrType.ShortDisplayName(), 
                        structuralType.ClrType.ShortDisplayName()));
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected abstract void OnBaseTypeChanged([NotNull] StructuralType originalBaseType);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected virtual void ValidateBaseTypeAllowed()
        {
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual ConfigurationSource? GetBaseTypeConfigurationSource() => _baseTypeConfigurationSource;

        private void UpdateBaseTypeConfigurationSource(ConfigurationSource configurationSource)
            => _baseTypeConfigurationSource = configurationSource.Max(_baseTypeConfigurationSource);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        // Note this is ISet because there is no suitable readonly interface in the profiles we are using
        public virtual ISet<StructuralType> GetDirectlyDerivedTypes() => _directlyDerivedTypes;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<StructuralType> GetDerivedTypes()
        {
            var derivedTypes = new List<StructuralType>();
            var type = this;
            var currentTypeIndex = 0;
            while (type != null)
            {
                derivedTypes.AddRange(type.GetDirectlyDerivedTypes());
                type = derivedTypes.Count > currentTypeIndex
                    ? derivedTypes[currentTypeIndex]
                    : null;
                currentTypeIndex++;
            }
            return derivedTypes;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<StructuralType> GetDerivedTypesInclusive()
            => new[] { this }.Concat(GetDerivedTypes());

        private bool InheritsFrom(StructuralType structuralType)
        {
            var et = this;

            do
            {
                if (structuralType == et)
                {
                    return true;
                }
            }
            while ((et = et._baseType) != null);

            return false;
        }

        /// <summary>
        ///     Runs the conventions when an annotation was set or removed.
        /// </summary>
        /// <param name="name"> The key of the set annotation. </param>
        /// <param name="annotation"> The annotation set. </param>
        /// <param name="oldAnnotation"> The old annotation. </param>
        /// <returns> The annotation that was set. </returns>
        protected override Annotation OnAnnotationSet(string name, Annotation annotation, Annotation oldAnnotation)
            => Model.ConventionDispatcher.OnEntityTypeAnnotationChanged(Builder, name, annotation, oldAnnotation);

        #region Primary and Candidate Keys
        
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Key AddKey([NotNull] Property property, ConfigurationSource configurationSource = ConfigurationSource.Explicit)
            => AddKey(new[] { property }, configurationSource);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Key AddKey(
            [NotNull] IReadOnlyList<Property> properties,
            ConfigurationSource configurationSource = ConfigurationSource.Explicit)
        {
            Check.NotEmpty(properties, nameof(properties));
            Check.HasNoNulls(properties, nameof(properties));

            if (_baseType != null)
            {
                throw new InvalidOperationException(CoreStrings.DerivedEntityTypeKey(this.DisplayName(), _baseType.DisplayName()));
            }

            for (var i = 0; i < properties.Count; i++)
            {
                var property = properties[i];
                for (var j = i + 1; j < properties.Count; j++)
                {
                    if (property == properties[j])
                    {
                        throw new InvalidOperationException(CoreStrings.DuplicatePropertyInList(Property.Format(properties), property.Name));
                    }
                }

                if (FindProperty(property.Name) != property
                    || property.Builder == null)
                {
                    throw new InvalidOperationException(CoreStrings.KeyPropertiesWrongEntity(Property.Format(properties), this.DisplayName()));
                }

                if (property.GetContainingForeignKeys().Any(k => k.DeclaringEntityType != this))
                {
                    throw new InvalidOperationException(CoreStrings.KeyPropertyInForeignKey(property.Name, this.DisplayName()));
                }

                if (property.IsNullable)
                {
                    throw new InvalidOperationException(CoreStrings.NullableKey(this.DisplayName(), property.Name));
                }
            }

            var key = FindKey(properties);
            if (key != null)
            {
                throw new InvalidOperationException(CoreStrings.DuplicateKey(Property.Format(properties), this.DisplayName(), key.DeclaringEntityType.DisplayName()));
            }

            key = new Key(properties, configurationSource);
            _keys.Add(properties, key);

            foreach (var property in properties)
            {
                if (property.Keys == null)
                {
                    property.Keys = new List<IKey> { key };
                }
                else
                {
                    property.Keys.Add(key);
                }
            }

            PropertyMetadataChanged();

            return Model.ConventionDispatcher.OnKeyAdded(key.Builder)?.Metadata;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Key GetOrAddKey([NotNull] Property property)
            => GetOrAddKey(new[] { property });

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Key GetOrAddKey([NotNull] IReadOnlyList<Property> properties)
            => FindKey(properties)
               ?? AddKey(properties);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Key FindKey([NotNull] IProperty property) => FindKey(new[] { property });

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Key FindKey([NotNull] IReadOnlyList<IProperty> properties)
        {
            Check.HasNoNulls(properties, nameof(properties));
            Check.NotEmpty(properties, nameof(properties));

            return FindDeclaredKey(properties) ?? _baseType?.FindKey(properties);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<Key> GetDeclaredKeys() => _keys.Values;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Key FindDeclaredKey([NotNull] IReadOnlyList<IProperty> properties)
            => _keys.TryGetValue(Check.NotEmpty(properties, nameof(properties)), out var key)
                ? key
                : null;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        // ReSharper disable once MethodOverloadWithOptionalParameter
        public virtual Key RemoveKey([NotNull] IReadOnlyList<IProperty> properties)
        {
            Check.NotEmpty(properties, nameof(properties));

            var key = FindDeclaredKey(properties);
            return key == null
                ? null
                : RemoveKey(key);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected virtual Key RemoveKey([NotNull] Key key)
        {
            CheckKeyNotInUse(key);

            _keys.Remove(key.Properties);
            key.Builder = null;

            foreach (var property in key.Properties)
            {
                if (property.Keys != null)
                {
                    property.Keys.Remove(key);
                    if (property.Keys.Count == 0)
                    {
                        property.Keys = null;
                    }
                }
            }

            PropertyMetadataChanged();

            Model.ConventionDispatcher.OnKeyRemoved(Builder, key);
            return key;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected virtual void CheckKeyNotInUse([NotNull] Key key)
        {
            var foreignKey = key.GetReferencingForeignKeys().FirstOrDefault();
            if (foreignKey != null)
            {
                throw new InvalidOperationException(
                    CoreStrings.KeyInUse(Property.Format(key.Properties), this.DisplayName(), foreignKey.DeclaringEntityType.DisplayName()));
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<Key> GetKeys() => _baseType?.GetKeys().Concat(_keys.Values) ?? _keys.Values;

        #endregion

        #region Foreign Keys

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual ForeignKey AddForeignKey(
            [NotNull] Property property,
            [NotNull] Key principalKey,
            [NotNull] EntityType principalEntityType,
            ConfigurationSource configurationSource = ConfigurationSource.Explicit)
            => AddForeignKey(new[] { property }, principalKey, principalEntityType, configurationSource);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual ForeignKey AddForeignKey(
            [NotNull] IReadOnlyList<Property> properties,
            [NotNull] Key principalKey,
            [NotNull] EntityType principalEntityType,
            ConfigurationSource? configurationSource = ConfigurationSource.Explicit)
        {
            Check.NotEmpty(properties, nameof(properties));
            Check.HasNoNulls(properties, nameof(properties));
            Check.NotNull(principalKey, nameof(principalKey));
            Check.NotNull(principalEntityType, nameof(principalEntityType));

            for (var i = 0; i < properties.Count; i++)
            {
                var property = properties[i];
                for (var j = i + 1; j < properties.Count; j++)
                {
                    if (property == properties[j])
                    {
                        throw new InvalidOperationException(CoreStrings.DuplicatePropertyInList(Property.Format(properties), property.Name));
                    }
                }

                var actualProperty = FindProperty(property.Name);
                if (actualProperty == null
                    || !actualProperty.DeclaringEntityType.IsAssignableFrom(property.DeclaringEntityType)
                    || property.Builder == null)
                {
                    throw new InvalidOperationException(CoreStrings.ForeignKeyPropertiesWrongEntity(Property.Format(properties), this.DisplayName()));
                }
            }

            ForeignKey.AreCompatible(
                principalEntityType,
                dependentEntityType: this,
                navigationToPrincipal: null,
                navigationToDependent: null,
                dependentProperties: properties,
                principalProperties: principalKey.Properties,
                unique: null,
                required: null,
                shouldThrow: true);

            var duplicateForeignKey = FindForeignKeysInHierarchy(properties, principalKey, principalEntityType).FirstOrDefault();
            if (duplicateForeignKey != null)
            {
                throw new InvalidOperationException(
                    CoreStrings.DuplicateForeignKey(
                        Property.Format(properties),
                        this.DisplayName(),
                        duplicateForeignKey.DeclaringEntityType.DisplayName(),
                        Property.Format(principalKey.Properties),
                        principalEntityType.DisplayName()));
            }

            var foreignKey = new ForeignKey(properties, principalKey, this, principalEntityType, configurationSource ?? ConfigurationSource.Convention);
            if (configurationSource.HasValue)
            {
                principalEntityType.UpdateConfigurationSource(configurationSource.Value);
                foreignKey.UpdateForeignKeyPropertiesConfigurationSource(configurationSource.Value);
                foreignKey.UpdatePrincipalKeyConfigurationSource(configurationSource.Value);
                foreignKey.UpdatePrincipalEndConfigurationSource(configurationSource.Value);
            }

            if (principalEntityType.Model != Model)
            {
                throw new InvalidOperationException(CoreStrings.EntityTypeModelMismatch(this.DisplayName(), principalEntityType.DisplayName()));
            }

            _foreignKeys.Add(foreignKey);

            foreach (var property in properties)
            {
                if (property.ForeignKeys == null)
                {
                    property.ForeignKeys = new List<IForeignKey> { foreignKey };
                }
                else
                {
                    property.ForeignKeys.Add(foreignKey);
                }
            }

            if (principalKey.ReferencingForeignKeys == null)
            {
                principalKey.ReferencingForeignKeys = new SortedSet<ForeignKey>(ForeignKeyComparer.Instance) { foreignKey };
            }
            else
            {
                var added = principalKey.ReferencingForeignKeys.Add(foreignKey);
                Debug.Assert(added);
            }

            if (principalEntityType.DeclaredReferencingForeignKeys == null)
            {
                principalEntityType.DeclaredReferencingForeignKeys = new SortedSet<ForeignKey>(ForeignKeyComparer.Instance) { foreignKey };
            }
            else
            {
                var added = principalEntityType.DeclaredReferencingForeignKeys.Add(foreignKey);
                Debug.Assert(added);
            }

            PropertyMetadataChanged();

            var builder = Model.ConventionDispatcher.OnForeignKeyAdded(foreignKey.Builder);

            foreignKey = builder?.Metadata;

            return foreignKey;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual ForeignKey GetOrAddForeignKey(
            [NotNull] Property property, [NotNull] Key principalKey, [NotNull] EntityType principalEntityType)
            => GetOrAddForeignKey(new[] { property }, principalKey, principalEntityType);

        // Note: this will return an existing foreign key even if it doesn't have the same referenced key
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual ForeignKey GetOrAddForeignKey(
            [NotNull] IReadOnlyList<Property> properties, [NotNull] Key principalKey, [NotNull] EntityType principalEntityType)
            => FindForeignKey(properties, principalKey, principalEntityType)
               ?? AddForeignKey(properties, principalKey, principalEntityType);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<ForeignKey> FindForeignKeys([NotNull] IProperty property)
            => FindForeignKeys(new[] { property });

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<ForeignKey> FindForeignKeys([NotNull] IReadOnlyList<IProperty> properties)
        {
            Check.HasNoNulls(properties, nameof(properties));
            Check.NotEmpty(properties, nameof(properties));

            return _baseType?.FindForeignKeys(properties)?.Concat(FindDeclaredForeignKeys(properties))
                   ?? FindDeclaredForeignKeys(properties);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual ForeignKey FindForeignKey(
            [NotNull] IProperty property,
            [NotNull] IKey principalKey,
            [NotNull] IEntityType principalEntityType)
            => FindForeignKey(new[] { property }, principalKey, principalEntityType);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual ForeignKey FindForeignKey(
            [NotNull] IReadOnlyList<IProperty> properties,
            [NotNull] IKey principalKey,
            [NotNull] IEntityType principalEntityType)
        {
            Check.HasNoNulls(properties, nameof(properties));
            Check.NotEmpty(properties, nameof(properties));
            Check.NotNull(principalKey, nameof(principalKey));
            Check.NotNull(principalEntityType, nameof(principalEntityType));

            return FindDeclaredForeignKey(properties, principalKey, principalEntityType)
                   ?? _baseType?.FindForeignKey(properties, principalKey, principalEntityType);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<ForeignKey> GetDeclaredForeignKeys() => _foreignKeys;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<ForeignKey> GetDerivedForeignKeys()
            => GetDerivedTypes().SelectMany(et => et.GetDeclaredForeignKeys());

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<ForeignKey> GetDerivedForeignKeysInclusive()
            => GetDeclaredForeignKeys().Concat(GetDerivedTypes().SelectMany(et => et.GetDeclaredForeignKeys()));

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<ForeignKey> FindDeclaredForeignKeys([NotNull] IReadOnlyList<IProperty> properties)
        {
            Check.NotEmpty(properties, nameof(properties));

            return _foreignKeys.Where(fk => PropertyListComparer.Instance.Equals(fk.Properties, properties));
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual ForeignKey FindDeclaredForeignKey(
            [NotNull] IReadOnlyList<IProperty> properties,
            [NotNull] IKey principalKey,
            [NotNull] IEntityType principalEntityType)
        {
            Check.NotEmpty(properties, nameof(properties));
            Check.NotNull(principalKey, nameof(principalKey));
            Check.NotNull(principalEntityType, nameof(principalEntityType));

            return FindDeclaredForeignKeys(properties).SingleOrDefault(
                fk =>
                    PropertyListComparer.Instance.Equals(fk.PrincipalKey.Properties, principalKey.Properties) &&
                    StringComparer.Ordinal.Equals(fk.PrincipalEntityType.Name, principalEntityType.Name));
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<ForeignKey> FindDerivedForeignKeys(
            [NotNull] IReadOnlyList<IProperty> properties)
            => GetDerivedTypes().SelectMany(et => et.FindDeclaredForeignKeys(properties));

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<ForeignKey> FindDerivedForeignKeys(
            [NotNull] IReadOnlyList<IProperty> properties,
            [NotNull] IKey principalKey,
            [NotNull] IEntityType principalEntityType)
            => GetDerivedTypes().Select(et => et.FindDeclaredForeignKey(properties, principalKey, principalEntityType))
                .Where(fk => fk != null);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<ForeignKey> FindForeignKeysInHierarchy(
            [NotNull] IReadOnlyList<IProperty> properties)
            => FindForeignKeys(properties).Concat(FindDerivedForeignKeys(properties));

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<ForeignKey> FindForeignKeysInHierarchy(
            [NotNull] IReadOnlyList<IProperty> properties,
            [NotNull] IKey principalKey,
            [NotNull] IEntityType principalEntityType)
            => ToEnumerable(FindForeignKey(properties, principalKey, principalEntityType))
                .Concat(FindDerivedForeignKeys(properties, principalKey, principalEntityType));

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual ForeignKey RemoveForeignKey(
            [NotNull] IReadOnlyList<IProperty> properties,
            [NotNull] IKey principalKey,
            [NotNull] IEntityType principalEntityType)
        {
            Check.NotEmpty(properties, nameof(properties));

            var foreignKey = FindDeclaredForeignKey(properties, principalKey, principalEntityType);
            return foreignKey == null
                ? null
                : RemoveForeignKey(foreignKey);
        }

        private ForeignKey RemoveForeignKey([NotNull] ForeignKey foreignKey)
        {
            if (foreignKey.DependentToPrincipal != null)
            {
                foreignKey.DeclaringEntityType.RemoveNavigation(foreignKey.DependentToPrincipal.Name);
            }

            if (foreignKey.PrincipalToDependent != null)
            {
                foreignKey.PrincipalEntityType.RemoveNavigation(foreignKey.PrincipalToDependent.Name);
            }

            var removed = _foreignKeys.Remove(foreignKey);
            foreignKey.Builder = null;

            foreach (var property in foreignKey.Properties)
            {
                if (property.ForeignKeys != null)
                {
                    property.ForeignKeys.Remove(foreignKey);
                    if (property.ForeignKeys.Count == 0)
                    {
                        property.ForeignKeys = null;
                    }
                }
            }

            foreignKey.PrincipalKey.ReferencingForeignKeys.Remove(foreignKey);
            foreignKey.PrincipalEntityType.DeclaredReferencingForeignKeys.Remove(foreignKey);

            PropertyMetadataChanged();

            if (removed)
            {
                if (foreignKey.DependentToPrincipal != null)
                {
                    Model.ConventionDispatcher.OnNavigationRemoved(
                        Builder,
                        foreignKey.PrincipalEntityType.Builder,
                        foreignKey.DependentToPrincipal.Name,
                        foreignKey.DependentToPrincipal.PropertyInfo);
                }

                if (foreignKey.PrincipalToDependent != null)
                {
                    Model.ConventionDispatcher.OnNavigationRemoved(
                        foreignKey.PrincipalEntityType.Builder,
                        Builder,
                        foreignKey.PrincipalToDependent.Name,
                        foreignKey.PrincipalToDependent.PropertyInfo);
                }

                Model.ConventionDispatcher.OnForeignKeyRemoved(Builder, foreignKey);

                return foreignKey;
            }

            return null;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<ForeignKey> GetReferencingForeignKeys()
            => _baseType?.GetReferencingForeignKeys().Concat(GetDeclaredReferencingForeignKeys())
               ?? GetDeclaredReferencingForeignKeys();

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<ForeignKey> GetDeclaredReferencingForeignKeys()
            => DeclaredReferencingForeignKeys ?? Enumerable.Empty<ForeignKey>();

        private SortedSet<ForeignKey> DeclaredReferencingForeignKeys { get; set; }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<ForeignKey> GetForeignKeys()
            => _baseType?.GetForeignKeys().Concat(_foreignKeys) ?? _foreignKeys;

        #endregion

        #region Navigations

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Navigation AddNavigation(
            [NotNull] string name,
            [NotNull] ForeignKey foreignKey,
            bool pointsToPrincipal)
        {
            Check.NotEmpty(name, nameof(name));
            Check.NotNull(foreignKey, nameof(foreignKey));

            return AddNavigation(new PropertyIdentity(name), foreignKey, pointsToPrincipal);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Navigation AddNavigation(
            [NotNull] PropertyInfo navigationProperty,
            [NotNull] ForeignKey foreignKey,
            bool pointsToPrincipal)
        {
            Check.NotNull(navigationProperty, nameof(navigationProperty));
            Check.NotNull(foreignKey, nameof(foreignKey));

            return AddNavigation(new PropertyIdentity(navigationProperty), foreignKey, pointsToPrincipal);
        }

        private Navigation AddNavigation(PropertyIdentity propertyIdentity, ForeignKey foreignKey, bool pointsToPrincipal)
        {
            var name = propertyIdentity.Name;
            var duplicateNavigation = FindNavigationsInHierarchy(name).FirstOrDefault();
            if (duplicateNavigation != null)
            {
                if (duplicateNavigation.ForeignKey != foreignKey)
                {
                    throw new InvalidOperationException(
                        CoreStrings.NavigationForWrongForeignKey(
                            duplicateNavigation.Name,
                            duplicateNavigation.DeclaringEntityType.DisplayName(),
                            Property.Format(foreignKey.Properties),
                            Property.Format(duplicateNavigation.ForeignKey.Properties)));
                }

                throw new InvalidOperationException(
                    CoreStrings.DuplicateNavigation(name, this.DisplayName(), duplicateNavigation.DeclaringEntityType.DisplayName()));
            }

            var duplicateProperty = FindPropertiesInHierarchy(name).FirstOrDefault();
            if (duplicateProperty != null)
            {
                throw new InvalidOperationException(
                    CoreStrings.ConflictingProperty(
                        name, this.DisplayName(),
                        duplicateProperty.DeclaringEntityType.DisplayName()));
            }

            Debug.Assert(
                !GetNavigations().Any(n => n.ForeignKey == foreignKey && n.IsDependentToPrincipal() == pointsToPrincipal),
                "There is another navigation corresponding to the same foreign key and pointing in the same direction.");

            Debug.Assert(
                (pointsToPrincipal ? foreignKey.DeclaringEntityType : foreignKey.PrincipalEntityType) == this,
                "EntityType mismatch");

            var navigationProperty = propertyIdentity.Property;
            if (ClrType != null)
            {
                Navigation.IsCompatible(
                    propertyIdentity.Name,
                    navigationProperty,
                    this,
                    pointsToPrincipal ? foreignKey.PrincipalEntityType : foreignKey.DeclaringEntityType,
                    !pointsToPrincipal && !foreignKey.IsUnique,
                    shouldThrow: true);
            }
            var navigation = new Navigation(name, propertyIdentity.Property, null, foreignKey);

            _navigations.Add(name, navigation);

            PropertyMetadataChanged();

            return navigation;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Navigation FindNavigation([NotNull] string name)
        {
            Check.NotEmpty(name, nameof(name));

            return FindDeclaredNavigation(name) ?? _baseType?.FindNavigation(name);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Navigation FindNavigation([NotNull] PropertyInfo propertyInfo)
            => FindNavigation(Check.NotNull(propertyInfo, nameof(propertyInfo)).Name);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Navigation FindDeclaredNavigation([NotNull] string name)
            => _navigations.TryGetValue(Check.NotEmpty(name, nameof(name)), out var navigation)
                ? navigation
                : null;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<Navigation> GetDeclaredNavigations() => _navigations.Values;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<Navigation> GetDerivedNavigations()
            => GetDerivedTypes().SelectMany(et => et.GetDeclaredNavigations());

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<Navigation> GetDerivedNavigationsInclusive()
            => GetDerivedTypesInclusive().SelectMany(et => et.GetDeclaredNavigations());

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<Navigation> FindDerivedNavigations([NotNull] string navigationName)
        {
            Check.NotNull(navigationName, nameof(navigationName));

            return GetDerivedTypes().Select(et => et.FindDeclaredNavigation(navigationName)).Where(n => n != null);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<Navigation> FindNavigationsInHierarchy([NotNull] string propertyName)
            => ToEnumerable(FindNavigation(propertyName)).Concat(FindDerivedNavigations(propertyName));

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Navigation RemoveNavigation([NotNull] string name)
        {
            Check.NotEmpty(name, nameof(name));

            var navigation = FindDeclaredNavigation(name);
            if (navigation == null)
            {
                return null;
            }

            _navigations.Remove(name);

            PropertyMetadataChanged();

            return navigation;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<Navigation> GetNavigations()
            => _baseType?.GetNavigations().Concat(_navigations.Values) ?? _navigations.Values;

        #endregion

        #region Indexes

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Index AddIndex(
            [NotNull] Property property,
            ConfigurationSource configurationSource = ConfigurationSource.Explicit)
            => AddIndex(new[] { property }, configurationSource);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Index AddIndex(
            [NotNull] IReadOnlyList<Property> properties,
            ConfigurationSource configurationSource = ConfigurationSource.Explicit)
        {
            Check.NotEmpty(properties, nameof(properties));
            Check.HasNoNulls(properties, nameof(properties));

            for (var i = 0; i < properties.Count; i++)
            {
                var property = properties[i];
                for (var j = i + 1; j < properties.Count; j++)
                {
                    if (property == properties[j])
                    {
                        throw new InvalidOperationException(CoreStrings.DuplicatePropertyInList(Property.Format(properties), property.Name));
                    }
                }

                if (FindProperty(property.Name) != property
                    || property.Builder == null)
                {
                    throw new InvalidOperationException(CoreStrings.IndexPropertiesWrongEntity(Property.Format(properties), this.DisplayName()));
                }
            }

            var duplicateIndex = FindIndexesInHierarchy(properties).FirstOrDefault();
            if (duplicateIndex != null)
            {
                throw new InvalidOperationException(CoreStrings.DuplicateIndex(Property.Format(properties), this.DisplayName(), duplicateIndex.DeclaringEntityType.DisplayName()));
            }

            var index = new Index(properties, this, configurationSource);

            _indexes.Add(properties, index);

            foreach (var property in properties)
            {
                if (property.Indexes == null)
                {
                    property.Indexes = new List<IIndex> { index };
                }
                else
                {
                    property.Indexes.Add(index);
                }
            }

            return Model.ConventionDispatcher.OnIndexAdded(index.Builder)?.Metadata;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Index GetOrAddIndex([NotNull] Property property)
            => GetOrAddIndex(new[] { property });

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Index GetOrAddIndex([NotNull] IReadOnlyList<Property> properties)
            => FindIndex(properties) ?? AddIndex(properties);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Index FindIndex([NotNull] IProperty property)
            => FindIndex(new[] { property });

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Index FindIndex([NotNull] IReadOnlyList<IProperty> properties)
        {
            Check.HasNoNulls(properties, nameof(properties));
            Check.NotEmpty(properties, nameof(properties));

            return FindDeclaredIndex(properties) ?? _baseType?.FindIndex(properties);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<Index> GetDeclaredIndexes() => _indexes.Values;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<Index> GetDerivedIndexes()
            => GetDerivedTypes().SelectMany(et => et.GetDeclaredIndexes());

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<Index> GetDerivedIndexesInclusive()
            => GetDeclaredIndexes().Concat(GetDerivedTypes().SelectMany(et => et.GetDeclaredIndexes()));

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Index FindDeclaredIndex([NotNull] IReadOnlyList<IProperty> properties)
            => _indexes.TryGetValue(Check.NotEmpty(properties, nameof(properties)), out var index)
                ? index
                : null;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<Index> FindDerivedIndexes([NotNull] IReadOnlyList<IProperty> properties)
            => GetDerivedTypes().Select(et => et.FindDeclaredIndex(properties)).Where(i => i != null);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<Index> FindIndexesInHierarchy([NotNull] IReadOnlyList<IProperty> properties)
            => ToEnumerable(FindIndex(properties)).Concat(FindDerivedIndexes(properties));

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Index RemoveIndex([NotNull] IReadOnlyList<IProperty> properties)
        {
            Check.NotEmpty(properties, nameof(properties));

            var index = FindDeclaredIndex(properties);
            return index == null
                ? null
                : RemoveIndex(index);
        }

        private Index RemoveIndex(Index index)
        {
            _indexes.Remove(index.Properties);
            index.Builder = null;

            foreach (var property in index.Properties)
            {
                if (property.Indexes != null)
                {
                    property.Indexes.Remove(index);
                    if (property.Indexes.Count == 0)
                    {
                        property.Indexes = null;
                    }
                }
            }

            Model.ConventionDispatcher.OnIndexRemoved(Builder, index);

            return index;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<Index> GetIndexes() => _baseType?.GetIndexes().Concat(_indexes.Values) ?? _indexes.Values;

        #endregion

        #region Properties

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Property AddProperty(
            [NotNull] string name,
            [CanBeNull] Type propertyType = null,
            // ReSharper disable once MethodOverloadWithOptionalParameter
            ConfigurationSource configurationSource = ConfigurationSource.Explicit,
            ConfigurationSource? typeConfigurationSource = ConfigurationSource.Explicit)
        {
            Check.NotNull(name, nameof(name));

            ValidateCanAddProperty(name);

            return AddProperty(
                name,
                propertyType,
                ClrType?.GetMembersInHierarchy(name).FirstOrDefault(),
                configurationSource,
                typeConfigurationSource);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Property AddProperty(
            [NotNull] MemberInfo memberInfo,
            ConfigurationSource configurationSource = ConfigurationSource.Explicit)
        {
            Check.NotNull(memberInfo, nameof(memberInfo));

            ValidateCanAddProperty(memberInfo.Name);

            if (ClrType == null)
            {
                throw new InvalidOperationException(CoreStrings.ClrPropertyOnShadowEntity(memberInfo.Name, this.DisplayName()));
            }

            if (memberInfo.DeclaringType == null
                || !memberInfo.DeclaringType.GetTypeInfo().IsAssignableFrom(ClrType.GetTypeInfo()))
            {
                throw new ArgumentException(
                    CoreStrings.PropertyWrongEntityClrType(
                        memberInfo.Name, this.DisplayName(), memberInfo.DeclaringType?.ShortDisplayName()));
            }

            return AddProperty(memberInfo.Name, memberInfo.GetMemberType(), memberInfo, configurationSource, configurationSource);
        }

        private void ValidateCanAddProperty(string name)
        {
            var duplicateProperty = FindPropertiesInHierarchy(name).FirstOrDefault();
            if (duplicateProperty != null)
            {
                throw new InvalidOperationException(
                    CoreStrings.DuplicateProperty(
                        name, this.DisplayName(), duplicateProperty.DeclaringEntityType.DisplayName()));
            }

            var duplicateNavigation = FindNavigationsInHierarchy(name).FirstOrDefault();
            if (duplicateNavigation != null)
            {
                throw new InvalidOperationException(
                    CoreStrings.ConflictingNavigation(
                        name, this.DisplayName(),
                        duplicateNavigation.DeclaringEntityType.DisplayName()));
            }
        }

        private Property AddProperty(
            string name,
            Type propertyType,
            MemberInfo memberInfo,
            ConfigurationSource configurationSource,
            ConfigurationSource? typeConfigurationSource)
        {
            Check.NotNull(name, nameof(name));

            if (propertyType == null)
            {
                if (memberInfo == null)
                {
                    throw new InvalidOperationException(CoreStrings.NoPropertyType(name, this.DisplayName()));
                }

                propertyType = memberInfo.GetMemberType();
            }
            else
            {
                if (memberInfo != null
                    && propertyType != memberInfo.GetMemberType())
                {
                    throw new InvalidOperationException(
                        CoreStrings.PropertyWrongClrType(
                            name,
                            this.DisplayName(),
                            memberInfo.GetMemberType().ShortDisplayName(),
                            propertyType.ShortDisplayName()));
                }
            }

            var property 
                = new Property(
                    name, 
                    propertyType, 
                    memberInfo as PropertyInfo, 
                    memberInfo as FieldInfo, 
                    this, 
                    configurationSource, 
                    typeConfigurationSource);

            _properties.Add(property.Name, property);
            PropertyMetadataChanged();

            return Model.ConventionDispatcher.OnPropertyAdded(property.Builder)?.Metadata;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Property GetOrAddProperty([NotNull] PropertyInfo propertyInfo)
            => FindProperty(propertyInfo) ?? AddProperty(propertyInfo);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Property GetOrAddProperty([NotNull] string name, [CanBeNull] Type propertyType)
            => FindProperty(name) ?? AddProperty(name, propertyType);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Property FindProperty([NotNull] PropertyInfo propertyInfo)
            => FindProperty(propertyInfo.Name);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Property FindProperty([NotNull] string name)
            => FindDeclaredProperty(Check.NotEmpty(name, nameof(name))) ?? _baseType?.FindProperty(name);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Property FindDeclaredProperty([NotNull] string name)
            => _properties.TryGetValue(Check.NotEmpty(name, nameof(name)), out var property)
                ? property
                : null;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<Property> GetDeclaredProperties() => _properties.Values;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<Property> FindDerivedProperties([NotNull] string propertyName)
        {
            Check.NotNull(propertyName, nameof(propertyName));

            return GetDerivedTypes().Select(et => et.FindDeclaredProperty(propertyName)).Where(p => p != null);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<Property> FindDerivedPropertiesInclusive([NotNull] string propertyName)
            => ToEnumerable(FindDeclaredProperty(propertyName)).Concat(FindDerivedProperties(propertyName));

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<Property> FindPropertiesInHierarchy([NotNull] string propertyName)
            => ToEnumerable(FindProperty(propertyName)).Concat(FindDerivedProperties(propertyName));

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Property RemoveProperty([NotNull] string name)
        {
            Check.NotEmpty(name, nameof(name));

            var property = FindDeclaredProperty(name);
            return property == null
                ? null
                : RemoveProperty(property);
        }

        private Property RemoveProperty(Property property)
        {
            CheckPropertyNotInUse(property);

            _properties.Remove(property.Name);
            property.Builder = null;

            PropertyMetadataChanged();

            return property;
        }

        private void CheckPropertyNotInUse(Property property)
        {
            var containingKey = property.Keys?.FirstOrDefault();
            if (containingKey != null)
            {
                throw new InvalidOperationException(
                    CoreStrings.PropertyInUseKey(property.Name, this.DisplayName(), Property.Format(containingKey.Properties)));
            }

            var containingForeignKey = property.ForeignKeys?.FirstOrDefault();
            if (containingForeignKey != null)
            {
                throw new InvalidOperationException(
                    CoreStrings.PropertyInUseForeignKey(
                        property.Name, this.DisplayName(),
                        Property.Format(containingForeignKey.Properties), containingForeignKey.DeclaringEntityType.DisplayName()));
            }

            var containingIndex = property.Indexes?.FirstOrDefault();
            if (containingIndex != null)
            {
                throw new InvalidOperationException(
                    CoreStrings.PropertyInUseIndex(
                        property.Name, this.DisplayName(),
                        Property.Format(containingIndex.Properties), containingIndex.DeclaringEntityType.DisplayName()));
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IEnumerable<Property> GetProperties()
            => _baseType?.GetProperties().Concat(_properties.Values) ?? _properties.Values;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override void PropertyMetadataChanged()
        {
            foreach (var property in GetProperties())
            {
                property.PropertyIndexes = null;
            }

            foreach (var navigation in GetNavigations())
            {
                navigation.PropertyIndexes = null;
            }

            // This path should only kick in when the model is still mutable and therefore access does not need
            // to be thread-safe.
            _counts = null;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual PropertyCounts Counts
            => NonCapturingLazyInitializer.EnsureInitialized(ref _counts, this, structuralType => ((IEntityType)structuralType).CalculateCounts());

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Func<InternalEntityEntry, ISnapshot> RelationshipSnapshotFactory
            => NonCapturingLazyInitializer.EnsureInitialized(
                ref _relationshipSnapshotFactory, this,
                structuralType => new RelationshipSnapshotFactoryFactory().Create((IEntityType)structuralType));

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Func<InternalEntityEntry, ISnapshot> OriginalValuesFactory
            => NonCapturingLazyInitializer.EnsureInitialized(
                ref _originalValuesFactory, this,
                structuralType => new OriginalValuesFactoryFactory().Create((IEntityType)structuralType));

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Func<ValueBuffer, ISnapshot> ShadowValuesFactory
            => NonCapturingLazyInitializer.EnsureInitialized(
                ref _shadowValuesFactory, this,
                structuralType => new ShadowValuesFactoryFactory().Create((IEntityType)structuralType));

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Func<ISnapshot> EmptyShadowValuesFactory
            => NonCapturingLazyInitializer.EnsureInitialized(
                ref _emptyShadowValuesFactory, this,
                structuralType => new EmptyShadowValuesFactoryFactory().CreateEmpty((IEntityType)structuralType));

        #endregion

        #region Ignore

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override ConfigurationSource? FindIgnoredMemberConfigurationSource(string name)
        {
            var ignoredSource = FindDeclaredIgnoredMemberConfigurationSource(name);

            return BaseType == null ? ignoredSource : BaseType.FindIgnoredMemberConfigurationSource(name).Max(ignoredSource);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override void OnTypeMemberIgnored(string name)
            => Model.ConventionDispatcher.OnEntityTypeMemberIgnored(Builder, name);

        #endregion

        #region Explicit interface implementations

        IModel ITypeBase.Model => Model;
        IMutableModel IMutableTypeBase.Model => Model;

        IStructuralType IStructuralType.BaseType => _baseType;

        IMutableStructuralType IMutableStructuralType.BaseType
        {
            get => _baseType;
            set => HasBaseType((StructuralType)value);
        }

        LambdaExpression IMutableStructuralType.QueryFilter
        {
            get => QueryFilter;
            set => QueryFilter = value;
        }

        IMutableKey IMutableStructuralType.AddKey(IReadOnlyList<IMutableProperty> properties)
            => AddKey(properties.Cast<Property>().ToList());

        IKey IStructuralType.FindKey(IReadOnlyList<IProperty> properties) => FindKey(properties);
        IMutableKey IMutableStructuralType.FindKey(IReadOnlyList<IProperty> properties) => FindKey(properties);
        IEnumerable<IKey> IStructuralType.GetKeys() => GetKeys();
        IEnumerable<IMutableKey> IMutableStructuralType.GetKeys() => GetKeys();
        IMutableKey IMutableStructuralType.RemoveKey(IReadOnlyList<IProperty> properties) => RemoveKey(properties);

        IMutableForeignKey IMutableStructuralType.AddForeignKey(
            IReadOnlyList<IMutableProperty> properties, IMutableKey principalKey, IMutableEntityType principalEntityType)
            => AddForeignKey(properties.Cast<Property>().ToList(), (Key)principalKey, (EntityType)principalEntityType);

        IMutableForeignKey IMutableStructuralType.FindForeignKey(
            IReadOnlyList<IProperty> properties, IKey principalKey, IEntityType principalEntityType)
            => FindForeignKey(properties, principalKey, principalEntityType);

        IForeignKey IStructuralType.FindForeignKey(IReadOnlyList<IProperty> properties, IKey principalKey, IEntityType principalEntityType)
            => FindForeignKey(properties, principalKey, principalEntityType);

        IEnumerable<IForeignKey> IStructuralType.GetForeignKeys() => GetForeignKeys();
        IEnumerable<IMutableForeignKey> IMutableStructuralType.GetForeignKeys() => GetForeignKeys();

        IMutableForeignKey IMutableStructuralType.RemoveForeignKey(
            IReadOnlyList<IProperty> properties, IKey principalKey, IEntityType principalEntityType)
            => RemoveForeignKey(properties, principalKey, principalEntityType);

        IMutableIndex IMutableStructuralType.AddIndex(IReadOnlyList<IMutableProperty> properties)
            => AddIndex(properties.Cast<Property>().ToList());

        IIndex IStructuralType.FindIndex(IReadOnlyList<IProperty> properties) => FindIndex(properties);
        IMutableIndex IMutableStructuralType.FindIndex(IReadOnlyList<IProperty> properties) => FindIndex(properties);
        IEnumerable<IIndex> IStructuralType.GetIndexes() => GetIndexes();
        IEnumerable<IMutableIndex> IMutableStructuralType.GetIndexes() => GetIndexes();

        IMutableIndex IMutableStructuralType.RemoveIndex(IReadOnlyList<IProperty> properties)
            => RemoveIndex(properties);

        IMutableProperty IMutableStructuralType.AddProperty(string name, Type propertyType) => AddProperty(name, propertyType);
        IProperty IStructuralType.FindProperty(string name) => FindProperty(name);
        IMutableProperty IMutableStructuralType.FindProperty(string name) => FindProperty(name);
        IEnumerable<IProperty> IStructuralType.GetProperties() => GetProperties();
        IEnumerable<IMutableProperty> IMutableStructuralType.GetProperties() => GetProperties();
        IMutableProperty IMutableStructuralType.RemoveProperty(string name) => RemoveProperty(name);

        #endregion

        private static IEnumerable<T> ToEnumerable<T>(T element)
            where T : class
            => element == null ? Enumerable.Empty<T>() : new[] { element };
    }
}
