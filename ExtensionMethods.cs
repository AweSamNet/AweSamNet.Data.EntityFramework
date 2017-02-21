using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using AweSamNet.Common.Caching;

namespace AweSamNet.Data.EntityFramework
{
    public static class ExtensionMethods
    {
        private static MemoryCache _cache = new MemoryCache(null);

        /// <summary>
        /// Adds a range of entities to a navigation property of another entity.
        /// </summary>
        /// <typeparam name="TEntity">Primary entity Type.</typeparam>
        /// <typeparam name="TProperty">Navigation property entity Type</typeparam>
        /// <param name="db">Database context to use.</param>
        /// <param name="entity">Primary entity.</param>
        /// <param name="collection">Collection to add to the navigation property.</param>
        /// <param name="key">Func to get the key from the entity.</param>
        /// <param name="navigationProperty">Func to get the navigation property of the entity.</param>
        public static void TryAddRangeToNavigationProperty<TEntity, TProperty>(this IDbContext db,
            TEntity entity,
            TProperty[] collection, Func<TProperty, object> key,
            Func<TEntity, ICollection<TProperty>> navigationProperty)
            where TEntity : class
            where TProperty : class, new()
        {
            db.Set<TEntity>().Attach(entity);
            db.AttachNavigationProperties(entity);

            //attach any existing entities
            foreach (var item in collection.Where(item => key(item) != key(new TProperty())))
            {
                var foundItem = db.Set<TProperty>().Find(key(item));

                if (navigationProperty(entity).All(t => key(t) != key(item)))
                {
                    navigationProperty(entity).Add(foundItem ?? item);
                }
            }
        }

        /// <summary>
        /// Upsert an entity based on the passed condition Func.
        /// </summary>
        public static TEntity Upsert<TEntity>(this IDbContext db, TEntity entity, Func<TEntity, bool> isNewCondition)
            where TEntity : class
        {
            db.Set<TEntity>().Attach(entity);
            db.AttachNavigationProperties(entity);
            db.Entry(entity).State = isNewCondition(entity)
                ? EntityState.Added
                : EntityState.Modified;

            return entity;
        }

        /// <summary>
        /// Attaches navigation properties and adds them only if new.  This method prevents trying to save existing navigation properties as Insert.
        /// </summary>
        /// <param name="db">Database context to use.</param>
        /// <param name="entity">Primary entity.</param>
        public static void AttachNavigationProperties(this IDbContext db, object entity)
        {
            Type type = entity.GetType();
            var references = _cache.GetOrAdd<IList<PropertyInfo>>(type.Name, () =>
            {
                var list = new List<PropertyInfo>();
                foreach (PropertyInfo propertyInfo in entity.GetType().GetProperties())
                {
                    var reference = db.Entry(entity).Member(propertyInfo.Name) as DbReferenceEntry;
                    if (reference != null) list.Add(propertyInfo);
                }
                return list;
            }, TimeSpan.FromMinutes(10)); // we only want to store types in local cache for 10 minutes since once every 10 minutes per entity type is not a large cost.

            foreach (var propertyInfo in references)
            {
                var reference = db.Entry(entity).Member(propertyInfo.Name) as DbReferenceEntry;
                var value = propertyInfo.GetValue(entity);
                var enumerable = value as IEnumerable;

                //we only want to load from the context in the event that the in-memory entity object actually has data here.
                if (reference != null && value != null && (enumerable == null || enumerable.Cast<object[]>().Any()))
                {
                    reference.Load();
                    //see if it exists
                    var entities = Enumerable.Cast<object>(reference.Query());
                    var subEntities = entities as object[] ?? entities.ToArray();
                    if (!subEntities.Any())
                    {
                        db.Set(reference.EntityEntry.Entity.GetType()).Add(reference.EntityEntry.Entity);
                    }

                    foreach ( var subEntity in subEntities)
                    {
                        db.AttachNavigationProperties(subEntity);
                    }
                }
            }
        }
    }
}
