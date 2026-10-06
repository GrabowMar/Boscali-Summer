using System;
using System.Reflection;
using Mirage;
using Mirage.Serialization;

namespace BoscaliSummer.Core.Net
{
    /// <summary>
    /// Installs a message's Mirage serializer pair (the game ships none for mod types) and registers
    /// the message. <see cref="Strict"/> throws when a Mirage seam is missing, so the module fails loudly
    /// at configure time; an instance built with a log tag logs
    /// <c>tag + " Mirage serializer seam X.Y" + missingTail</c> instead and the module carries on
    /// without replication.
    /// </summary>
    internal sealed class MirageSerializers
    {
        internal static readonly MirageSerializers Strict = new MirageSerializers(null, null);

        private readonly string tag, missingTail;

        internal MirageSerializers(string tag, string missingTail)
        {
            this.tag = tag;
            this.missingTail = missingTail;
        }

        internal void Install<T>(Action<NetworkWriter, T> write, Func<NetworkReader, T> read)
        {
            Bind(typeof(Writer<T>), "Write", write);
            Bind(typeof(Reader<T>), "Read", read);
            MessagePacker.RegisterMessage<T>();
        }

        private void Bind(Type holder, string property, object value)
        {
            PropertyInfo target = holder.GetProperty(
                property, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (target == null)
            {
                if (tag == null) throw new MissingMemberException(holder.FullName, property);
                Plugin.Logger.LogError(tag + " Mirage serializer seam " + holder.Name + "." + property + missingTail);
                return;
            }
            target.SetValue(null, value, null);
        }
    }

    /// <summary>
    /// One side's message registrations (server or client). <see cref="Swap"/> is called on a slow
    /// poll with the live <see cref="MessageHandler"/> (or null) and moves the registrations when
    /// the connection changed.
    /// </summary>
    internal sealed class HandlerSlot
    {
        private readonly Action<MessageHandler> register;
        private readonly Action<MessageHandler> unregister;

        private HandlerSlot(Action<MessageHandler> register, Action<MessageHandler> unregister)
        {
            this.register = register;
            this.unregister = unregister;
        }

        internal MessageHandler Current { get; private set; }

        internal static HandlerSlot Of<T>(Action<INetworkPlayer, T> receive) => new HandlerSlot(
            h => h.RegisterHandler<T>((p, m) => receive(p, m), false),
            h => h.UnregisterHandler<T>());

        internal static HandlerSlot Of<T1, T2>(Action<INetworkPlayer, T1> receive1, Action<INetworkPlayer, T2> receive2) =>
            new HandlerSlot(
                h => { h.RegisterHandler<T1>((p, m) => receive1(p, m), false); h.RegisterHandler<T2>((p, m) => receive2(p, m), false); },
                h => { h.UnregisterHandler<T1>(); h.UnregisterHandler<T2>(); });

        internal static HandlerSlot Of<T1, T2, T3>(Action<INetworkPlayer, T1> receive1,
            Action<INetworkPlayer, T2> receive2, Action<INetworkPlayer, T3> receive3) =>
            new HandlerSlot(
                h =>
                {
                    h.RegisterHandler<T1>((p, m) => receive1(p, m), false);
                    h.RegisterHandler<T2>((p, m) => receive2(p, m), false);
                    h.RegisterHandler<T3>((p, m) => receive3(p, m), false);
                },
                h => { h.UnregisterHandler<T1>(); h.UnregisterHandler<T2>(); h.UnregisterHandler<T3>(); });

        /// <summary>True when the handler changed (old registrations dropped, new ones made).</summary>
        internal bool Swap(MessageHandler next)
        {
            if (next == Current) return false;
            if (Current != null) unregister(Current);
            Current = next;
            if (next != null) register(next);
            return true;
        }

        internal void Release() => Swap(null);
    }
}
