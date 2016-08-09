---
title: Azure Service Bus Transport Transaction Support
reviewed: 2016-04-20
tags:
- Azure
- Cloud
- Transactions
---


## Transactions and delivery guarantees

### Versions 6 and below

NServiceBus relies on transaction scopes to control how it behaves when it comes to transactions, message dispatching and rollback guarantees. 

The Azure Service Bus library, which is used in the Azure Service Bus transport, has a few requirements, when it is used inside a transaction scope, that must be taken into account.
* It requires the use of the `Serializable` isolation level when it's used inside a transaction scope (the most restrictive isolation level that does not permit `dirty reads`, `phantom reads` and `non repeatable reads`; will block any reader until the writer is committed). For more information refer to [Transaction Isolation Levels Explained in Details](http://dotnetspeak.com/2013/04/transaction-isolation-levels-explained-in-details) article. NServiceBus Azure Service Bus transport configuration is therefore hard-coded to `Serializable` isolation level. Users can't override it.
* It also proactively checks to see whether it is used alone inside a transaction and it will throw an exception if used together with another transactional resource such as a database connection. To allow the user code to still use a transaction for it's database connections, in another transaction scope provided by NServiceBus around a handler, the 2 scopes are kept apart and synchronized using an implementation of `IEnlistmentNotification` for both the send and completion operations.
* Finally, it does not allow send operation on different messaging entities in the same transaction. So for example a send to Queue1 and a send to Queue2 in the same scope, will throw an exception. Again this problem is negated using separate instances of `IEnlistmentNotification` on each send operation.

The benefit of this approach is that user code can use database connections and send messages to multiple endpoints while maintaining the aspects expected from a transaction. However the downside is that each operation is individual. Even though they are executed at the same moment in a transaction, it can happen in theory that one of the operations fails, while the others do not. This problem is also mitigated by using multiple layers of retry behavior to reduce the chance, so a short interruption in connectivity or broker service will not have a significant impact and operations will eventually succeed. However a full outage at the wrong moment could lead to f.e. a database record being written, while a send operation did not. 

The maximum guarantee that the transport can deliver in these versions is in effect `ReceiveOnly`. 

Enabling/Disabling DTC will have no effect, this setting is ignored either way as Azure Service Bus does not support it.

Disabling transactions will also turn off the `PeekLock` mechanism, so that the transport works completely transaction less and shows no retry behavior. This is highly unrecommended!
  

### Versions 7 and above

In NServiceBus version 6 and above, a transaction scope is no longer mandatory and the concept of transaction mode has become explicit. Azure Service Bus Transport now supports `SendAtomicWithReceive`, `ReceiveOnly` and `None` levels.

#### Transport transaction - Sends atomic with Receive

Note: `SendAtomicWithReceive` level is supported only when destination and receive queues are in the same namespace.

The `SendAtomicWithReceive` guarantee is achieved by using `ViaEntityPath` property on outbound messages. It's value is set to the receiving queue.

If the `ViaEntityPath` is not empty, then messages will be added to the receive queue. The messages will be forwarded to their actual destination (inside the broker) only when the complete operation is called on the received brokered message. The message won't be forwarded if the lock duration limit is exceeded (30 seconds by default) or if the message is explicitly abandoned.


#### Transport transaction - Receive Only

The `ReceiveOnly` guarantee is based on the Azure Service Bus Peek-Lock mechanism.

The message is not removed from the queue directly after receive, but it's hidden by default for 30 seconds. That prevents other instances from picking it up. If the receiver fails to process the message withing that timeframe or explicitly abandons the message, then the message will become visible again. Other instances will be able to pick it up.


#### Unreliable (Transactions Disabled)

When transactions are disabled then NServiceBus uses the [ASB's ReceiveAndDelete mode](https://msdn.microsoft.com/en-us/library/microsoft.servicebus.messaging.receivemode.aspx).

The message is deleted from the queue directly after receive operation completes, before it is processed.
