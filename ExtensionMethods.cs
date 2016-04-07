using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AweSamNet.Data.EntityFramework
{
    public static class ExtensionMethods
    {
        public static void TryAddRangeToNavigationProperty<T, TProperty>(this IDbContext db, T entity,
            TProperty[] collection, Func<TProperty, object> key, Func<T, ICollection<TProperty>> navigationProperty)
            where T : class
            where TProperty : class, new()
        {
            db.Set<T>().Attach(entity);

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
    }
}
