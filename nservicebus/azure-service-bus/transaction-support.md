---
title: Azure Service Bus Transport Transaction Support
reviewed: 2016-04-20
tags:
- Azure
- Cloud
- Transactions
---

## Summary

#### Transport transaction - Sends atomic with Receive

Note: `SendAtomicWithReceive` level is supported only as from version 7 and when destination and receive queues are in the same namespace.

The `SendAtomicWithReceive` guarantee is achieved by using `ViaEntityPath` property on outbound message senders. It's value is set to the receiving queue.

If the `ViaEntityPath` is not empty, then messages will be added to the receive queue. The messages will be forwarded to their actual destination (inside the broker) only when the complete operation is called on the received brokered message. The message won't be forwarded if the lock duration limit is exceeded (30 seconds by default) or if the message is explicitly abandoned.


#### Transport transaction - Receive Only

The `ReceiveOnly` guarantee is based on the Azure Service Bus Peek-Lock mechanism.

The message is not removed from the queue directly after receive, but it's hidden for, by default , 30 seconds. That prevents other instances from picking it up. If the receiver fails to process the message withing that timeframe or explicitly abandons the message, then the message will become visible again. Other instances will be able to pick it up (effectively works like a rollback).


#### Unreliable (Transactions Disabled)

When transactions are disabled then NServiceBus the transport uses the [ASB's ReceiveAndDelete mode](https://msdn.microsoft.com/en-us/library/microsoft.servicebus.messaging.receivemode.aspx).

The message is deleted from the queue directly after receive operation completes, before it is processed.

This mode is highly unrecommended!


## Understanding internal transactions and delivery guarantees

### Versions 6 and below

NServiceBus relies on transaction scopes to control how it behaves when it comes to transactions, message dispatching and commit/rollback guarantees. The architecture is schematically represented like this:

[TODO: insert picture 1]

The Azure Service Bus library, which is used in the Azure Service Bus transport, has a few requirements, when it is used inside a transaction scope, that must be taken into account.
* It requires the use of the `Serializable` isolation level when it's used inside a transaction scope (the most restrictive isolation level that does not permit `dirty reads`, `phantom reads` and `non repeatable reads`; will block any reader until the writer is committed). For more information refer to [Transaction Isolation Levels Explained in Details](http://dotnetspeak.com/2013/04/transaction-isolation-levels-explained-in-details) article. NServiceBus Azure Service Bus transport configuration is therefore hard-coded to `Serializable` isolation level. Users can't override it.
* It also proactively checks to see whether it is used alone inside a transaction and it will throw an exception if used together with another transactional resource such as a database connection. To allow the user code to still use a transaction for it's database connections, in another transaction scope provided by NServiceBus around a handler, the 2 scopes are kept apart and synchronized using an implementation of `IEnlistmentNotification` for both the send and completion operations. (Schematically represented by the orange to yellow star notation in the diagram)
* Finally, it does not allow send operation on different messaging entities in the same transaction. So for example a send to Queue1 and a send to Queue2 in the same scope, will throw an exception. Again this problem is negated using separate instances of `IEnlistmentNotification` on each send operation. 

The benefit of this approach is that user code can use database connections and send messages to multiple endpoints while maintaining the aspects expected from a transaction. However the downside is that each operation is individual. Even though they are executed at the same moment in a transaction, it can happen in theory that one of the operations fails, while the others do not. This problem is again mitigated by using multiple layers of retry behavior to reduce the chance, so a short interruption in connectivity or broker outage will not have a significant impact and operations will eventually succeed. However a full outage right after the database transaction committed could lead to a send operation that did not execute. 

The maximum guarantee that the transport can deliver in these versions is in effect `ReceiveOnly`. 

Enabling/Disabling DTC will have no effect, this setting is ignored either way as Azure Service Bus does not support it.

Disabling transactions will also turn off the `PeekLock` mechanism, so that the transport immediately completes any incoming message before processing and shows no retry behavior. 
  

### Versions 7 and above

In NServiceBus version 6 and above, the shape of the pipeline has changed so that a transaction scope is no longer mandatory in order to orchestrate dispatching of messages with complete/rollback behavior. The new architecture is schematically represented like this:

[TODO: insert picture 2]

Note the fork in the pipeline which is separating the user code invocation path from the dispatching path. By putting a suppress scope around this section of the pipeline, the transport can prevent any other transactional resource from enlisting in the scope.

Furthermore the Azure Service Bus transport now also takes advantage of a little known capability if the Azure Service Bus SDK, the via entity path / transfer queue. Using this feature, send operations to different Azure Service Bus entities can be executed via a single entity, usually the receive queue. Schematically it works like this:

[TODO: insert picture 3]

Combining these capabilities, allows Azure Service Bus Transport to support `SendAtomicWithReceive`, `ReceiveOnly` and `None` transaction mode levels.

