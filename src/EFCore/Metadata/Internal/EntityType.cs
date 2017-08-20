// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Collections.Generic;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore.Metadata.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class EntityType : StructuralType, IMutableEntityType
    {
        private Key _primaryKey;

        private ChangeTrackingStrategy? _changeTrackingStrategy;

        private ConfigurationSource? _primaryKeyConfigurationSource;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public EntityType([NotNull] string name, [NotNull] Model model, ConfigurationSource configurationSource)
            : base(name, model, configurationSource)
        {
            Builder = new InternalEntityTypeBuilder(this, model.Builder);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public EntityType([NotNull] Type clrType, [NotNull] Model model, ConfigurationSource configurationSource)
            : base(clrType, model, configurationSource)
        {
            Check.ValidEntityType(clrType, nameof(clrType));

            Builder = new InternalEntityTypeBuilder(this, model.Builder);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public EntityType(
            [NotNull] string name,
            [NotNull] Model model,
            [NotNull] string definingNavigationName,
            [NotNull] EntityType definingEntityType,
            ConfigurationSource configurationSource)
            : this(name, model, configurationSource)
        {
            Builder = new InternalEntityTypeBuilder(this, model.Builder);
            DefiningNavigationName = definingNavigationName;
            DefiningEntityType = definingEntityType;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public EntityType(
            [NotNull] Type clrType,
            [NotNull] Model model,
            [NotNull] string definingNavigationName,
            [NotNull] EntityType definingEntityType,
            ConfigurationSource configurationSource)
            : this(clrType, model, configurationSource)
        {
            Builder = new InternalEntityTypeBuilder(this, model.Builder);
            DefiningNavigationName = definingNavigationName;
            DefiningEntityType = definingEntityType;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual string DefiningNavigationName { get; }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual EntityType DefiningEntityType { get; }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override IComparer<string> CreatePropertyComparer()
        {
            return new PropertyComparer(this);
        }       
        
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override IComparer<StructuralType> CreatePathComparer()
        {
            return EntityTypePathComparer.Instance;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        new public virtual EntityType BaseType => (EntityType)base.BaseType;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual EntityType RootType() => (EntityType)((IEntityType)this).RootType();

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override void OnBaseTypeChanged(StructuralType originalBaseType)
        {
            Model.ConventionDispatcher.OnBaseEntityTypeChanged(Builder, (EntityType)originalBaseType);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override void ValidateBaseTypeWhenHasClrType(StructuralType structuralType)
        {
            base.ValidateBaseTypeWhenHasClrType(structuralType);

            var entityType = (EntityType)structuralType;

            if (entityType.HasDefiningNavigation())
            {
                throw new InvalidOperationException(
                    CoreStrings.DependentBaseType(this.DisplayName(), entityType.DisplayName()));
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override void ValidateBaseTypeAllowed()
        {
            base.ValidateBaseTypeAllowed();

            if (this.HasDefiningNavigation())
            {
                throw new InvalidOperationException(CoreStrings.DependentDerivedType(this.DisplayName()));
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override string ToString() => this.ToDebugString();        
        
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual DebugView<EntityType> DebugView
            => new DebugView<EntityType>(this, m => m.ToDebugString(false));

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual ChangeTrackingStrategy ChangeTrackingStrategy
        {
            get => _changeTrackingStrategy ?? Model.ChangeTrackingStrategy;
            set
            {
                var errorMessage = this.CheckChangeTrackingStrategy(value);
                if (errorMessage != null)
                {
                    throw new InvalidOperationException(errorMessage);
                }

                _changeTrackingStrategy = value;

                PropertyMetadataChanged();
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Key SetPrimaryKey([CanBeNull] Property property)
            => SetPrimaryKey(property == null ? null : new[] { property });

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Key SetPrimaryKey(
            [CanBeNull] IReadOnlyList<Property> properties,
            ConfigurationSource configurationSource = ConfigurationSource.Explicit)
        {
            if (BaseType != null)
            {
                throw new InvalidOperationException(CoreStrings.DerivedEntityTypeKey(this.DisplayName(), BaseType.DisplayName()));
            }

            var oldPrimaryKey = _primaryKey;
            if (oldPrimaryKey == null
                && (properties?.Count ?? 0) == 0)
            {
                return null;
            }

            Key newKey = null;
            if ((properties?.Count ?? 0) != 0)
            {
                newKey = GetOrAddKey(properties);
                if (oldPrimaryKey == newKey)
                {
                    UpdatePrimaryKeyConfigurationSource(configurationSource);
                    return newKey;
                }
            }

            if (oldPrimaryKey != null)
            {
                foreach (var property in _primaryKey.Properties)
                {
                    Properties.Remove(property.Name);
                    property.PrimaryKey = null;
                }

                _primaryKey = null;

                foreach (var property in oldPrimaryKey.Properties)
                {
                    Properties.Add(property.Name, property);
                }
            }

            if ((properties?.Count ?? 0) != 0)
            {
                foreach (var property in newKey.Properties)
                {
                    Properties.Remove(property.Name);
                    property.PrimaryKey = newKey;
                }

                _primaryKey = newKey;

                foreach (var property in newKey.Properties)
                {
                    Properties.Add(property.Name, property);
                }

                UpdatePrimaryKeyConfigurationSource(configurationSource);
            }
            else
            {
                SetPrimaryKeyConfigurationSource(null);
            }

            PropertyMetadataChanged();
            Model.ConventionDispatcher.OnPrimaryKeyChanged(Builder, oldPrimaryKey);

            return _primaryKey;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Key GetOrSetPrimaryKey([NotNull] Property property)
            => GetOrSetPrimaryKey(new[] { property });

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Key GetOrSetPrimaryKey([NotNull] IReadOnlyList<Property> properties)
            => FindPrimaryKey(properties) ?? SetPrimaryKey(properties);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Key FindPrimaryKey()
            => BaseType?.FindPrimaryKey() ?? FindDeclaredPrimaryKey();

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Key FindDeclaredPrimaryKey() => _primaryKey;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Key FindPrimaryKey([CanBeNull] IReadOnlyList<Property> properties)
        {
            Check.HasNoNulls(properties, nameof(properties));
            Check.NotEmpty(properties, nameof(properties));

            if (BaseType != null)
            {
                return BaseType.FindPrimaryKey(properties);
            }

            if (_primaryKey != null
                && PropertyListComparer.Instance.Compare(_primaryKey.Properties, properties) == 0)
            {
                return _primaryKey;
            }

            return null;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual ConfigurationSource? GetPrimaryKeyConfigurationSource() => _primaryKeyConfigurationSource;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        private void SetPrimaryKeyConfigurationSource(ConfigurationSource? configurationSource)
            => _primaryKeyConfigurationSource = configurationSource;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        private void UpdatePrimaryKeyConfigurationSource(ConfigurationSource configurationSource)
            => _primaryKeyConfigurationSource = configurationSource.Max(_primaryKeyConfigurationSource);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override Key RemoveKey(Key key)
        {
            CheckKeyNotInUse(key);

            if (_primaryKey == key)
            {
                SetPrimaryKey((IReadOnlyList<Property>)null);
                _primaryKeyConfigurationSource = null;
            }

            return base.RemoveKey(key);
        }

        #region Explicit interface implementations

        IModel ITypeBase.Model => Model;
        IMutableModel IMutableTypeBase.Model => Model;
        IEntityType IEntityType.BaseType => BaseType;

        IMutableEntityType IMutableEntityType.BaseType
        {
            get => BaseType;
            set => HasBaseType((EntityType)value);
        }

        IEntityType IEntityType.DefiningEntityType => DefiningEntityType;

        IMutableKey IMutableEntityType.SetPrimaryKey(IReadOnlyList<IMutableProperty> properties)
            => SetPrimaryKey(properties?.Cast<Property>().ToList());

        IKey IEntityType.FindPrimaryKey() => FindPrimaryKey();
        IMutableKey IMutableEntityType.FindPrimaryKey() => FindPrimaryKey();
        
        #endregion

        private class PropertyComparer : IComparer<string>
        {
            private readonly EntityType _entityType;

            public PropertyComparer(EntityType entityType)
            {
                _entityType = entityType;
            }

            public int Compare(string x, string y)
            {
                var properties = _entityType.FindPrimaryKey()?.Properties.Select(p => p.Name).ToList();

                var xIndex = -1;
                var yIndex = -1;

                if (properties != null)
                {
                    xIndex = properties.IndexOf(x);
                    yIndex = properties.IndexOf(y);
                }

                // Neither property is part of the Primary Key
                // Compare the property names
                if (xIndex == -1
                    && yIndex == -1)
                {
                    return StringComparer.Ordinal.Compare(x, y);
                }

                // Both properties are part of the Primary Key
                // Compare the indices
                if (xIndex > -1
                    && yIndex > -1)
                {
                    return xIndex - yIndex;
                }

                // One property is part of the Primary Key
                // The primary key property is first
                return xIndex > yIndex
                    ? -1
                    : 1;
            }
        }

    }
}
