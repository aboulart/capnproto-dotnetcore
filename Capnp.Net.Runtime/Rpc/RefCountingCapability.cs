﻿using System;
using System.Threading;
using System.Threading.Tasks;

namespace Capnp.Rpc
{
    abstract class RefCountingCapability: ConsumedCapability
    {
        // Note on reference counting: Works in analogy to COM. AddRef() adds a reference,
        // Release() removes it. When the reference count reaches zero, the capability must be
        // released remotely, i.e. we need to tell the remote peer that it can remove this
        // capability from its export table. To be on the safe side, 
        // this class also implements a finalizer which will auto-release this capability
        // remotely. This might happen if one forgets to Dispose() a Proxy. It might also happen
        // if no Proxy is ever created. The latter situation occurs if the using client never
        // deserializes the capability Proxy from its RPC state. This situation is nearly
        // impossible to handle without relying on GC, since we never know when deserialization
        // happens, and there is no RAII like in C++. Since this situation is expected to happen rarely, 
        // it seems acceptable to rely on the finalizer. There are three possible states.
        // A: Initial state after construction: No reference, capability is *not* released.
        // B: Some positive reference count.
        // C: Released state: No reference anymore, capability *is* released.
        // In order to distinguish state A from C, the member _refCount stores the reference count *plus one*. 
        // Value 0 has the special meaning of being in state C.
        int _refCount = 1;

        ~RefCountingCapability()
        {
            Dispose(false);
        }

        /// <summary>
        /// Part of the Dispose pattern implementation.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _refCount = 0;

                try
                {
                    ReleaseRemotely();
                }
                catch
                {
                }
            }
            else
            {
                if (_refCount > 0)
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            ReleaseRemotely();
                        }
                        catch
                        {
                        }
                    });
                }
            }
        }

        internal sealed override void AddRef()
        {
            if (Interlocked.Increment(ref _refCount) <= 1)
            {
                throw new ObjectDisposedException(nameof(ConsumedCapability));
            }
        }

        internal sealed override void Release()
        {
            if (1 >= Interlocked.Decrement(ref _refCount))
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }
        }
    }
}
