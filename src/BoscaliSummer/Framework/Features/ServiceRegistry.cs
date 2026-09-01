using System;
using System.Collections.Generic;

namespace BoscaliSummer.Framework.Features
{
    internal sealed class ServiceRegistry
    {
        private readonly Dictionary<Type, object> services = new Dictionary<Type, object>();

        public void Add<T>(T service) where T : class
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            Type key = typeof(T);
            if (services.ContainsKey(key))
                throw new InvalidOperationException("A service is already registered for " + key.FullName + ".");
            services.Add(key, service);
        }

        public bool TryGet<T>(out T service) where T : class
        {
            if (services.TryGetValue(typeof(T), out object value))
            {
                service = (T)value;
                return true;
            }
            service = null;
            return false;
        }

        public T GetRequired<T>() where T : class
        {
            if (TryGet(out T service)) return service;
            throw new InvalidOperationException("Required service is not registered: " + typeof(T).FullName + ".");
        }

        internal void Remove(Type serviceType) => services.Remove(serviceType);
        internal void Clear() => services.Clear();
    }
}
