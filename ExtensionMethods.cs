using System;
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
        /// <param name="navigationProperty">Funct to get the navigation property of the entity.</param>
        public static void TryAddRangeToNavigationProperty<TEntity, TProperty>(this IDbContext db,
            TEntity entity,
            TProperty[] collection, Func<TProperty, object> key,
            Func<TEntity, ICollection<TProperty>> navigationProperty)
            where TEntity : class
            where TProperty : class, new()
        {
            db.Set<TEntity>().Attach(entity);

            //attach any existing entities
            foreach (var item in collection.Where(item => key(item) != key(new TProperty())))
            {
                db.Set<TProperty>().Attach(item);
                if (navigationProperty(entity).All(t => key(t) != key(item)))
                {
                    navigationProperty(entity).Add(item);
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
        /// <typeparam name="TEntity">Primary entity Type.</typeparam>
        /// <param name="db">Database context to use.</param>
        /// <param name="entity">Primary entity.</param>
        public static void AttachNavigationProperties<TEntity>(this IDbContext db, TEntity entity)
            where TEntity : class
        {
            Type type = typeof (TEntity);
            var references = _cache.GetOrAdd<IList<string>>(type.Name, () =>
            {
                var list = new List<string>();
                foreach (PropertyInfo propertyInfo in entity.GetType().GetProperties())
                {
                    var reference = db.Entry(entity).Member(propertyInfo.Name) as DbReferenceEntry;
                    if (reference != null) list.Add(propertyInfo.Name);
                }
                return list;
            }, TimeSpan.FromMinutes(10)); // we only want to store types in local cache for 10 minutes since once every 10 minutes per entity type is not a large cost.

            foreach (var referenceName in references)
            {
                var reference = db.Entry(entity).Member(referenceName) as DbReferenceEntry;
                if (reference != null)
                {
                    reference.Load();
                    //see if it exists
                    if (!Enumerable.Cast<object>(reference.Query()).Any())
                    {
                        db.Set(reference.CurrentValue.GetType()).Add(reference.CurrentValue);
                    }
                }
            }
        }
    }
}
