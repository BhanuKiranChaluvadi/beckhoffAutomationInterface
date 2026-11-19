using System;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>A COM interface whose calls go to a function: exercises the real interface call
    /// path without starting XAE. The proxy casts to any interface, as a TwinCAT tree item that
    /// is also an ITcPlcDeclaration.</summary>
    sealed class ComStub : RealProxy, IRemotingTypeInfo
    {
        readonly Func<IMethodCallMessage, object> invoke;
        ComStub(Type type, Func<IMethodCallMessage, object> invoke) : base(type) { this.invoke = invoke; }

        public static T Make<T>(Func<IMethodCallMessage, object> invoke) =>
            (T)new ComStub(typeof(T), invoke).GetTransparentProxy();

        public string TypeName { get; set; }
        public bool CanCastTo(Type fromType, object o) => fromType.IsInterface;

        public override IMessage Invoke(IMessage message)
        {
            var call = (IMethodCallMessage)message;
            try { return new ReturnMessage(invoke(call), null, 0, call.LogicalCallContext, call); }
            catch (Exception error) { return new ReturnMessage(error, call); }
        }
    }
}
