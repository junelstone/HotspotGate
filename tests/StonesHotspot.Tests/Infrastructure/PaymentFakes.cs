using System.Collections.Concurrent;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Payments;

namespace StonesHotspot.Tests.Infrastructure;

/// <summary>A simulated operator: records payment requests and answers status checks with what the test sets.</summary>
public sealed class FakePayments : IPaymentProviderFactory
{
    public ConcurrentQueue<PaymentRequest> Requests { get; } = new();
    public ConcurrentDictionary<string, OperatorPaymentStatus> States { get; } = new();
    public string? RequestError { get; set; }
    public int StatusCalls;

    public IPaymentProvider? Create(PaymentMethod method, PaymentSettings settings) =>
        (method == PaymentMethod.MtnMoMo ? settings.Mtn.Configured : settings.Airtel.Configured) ? new Provider(method, this) : null;

    public PaymentRequest Last => Requests.Last();

    public void Pay(string reference, string operatorId = "OP-1", decimal? amount = null) =>
        States[reference] = new OperatorPaymentStatus(OperatorPaymentState.Successful, operatorId, null, amount);

    public void Decline(string reference, string reason = "APPROVAL_REJECTED") =>
        States[reference] = new OperatorPaymentStatus(OperatorPaymentState.Failed, null, reason);

    private sealed class Provider(PaymentMethod method, FakePayments owner) : IPaymentProvider
    {
        public PaymentMethod Method => method;

        public string NewReference() => Guid.NewGuid().ToString();

        public Task RequestToPayAsync(PaymentRequest request, CancellationToken ct)
        {
            if (owner.RequestError is { } error)
                throw new PaymentException(error);
            owner.Requests.Enqueue(request);
            return Task.CompletedTask;
        }

        public Task<OperatorPaymentStatus> GetStatusAsync(string reference, CancellationToken ct)
        {
            Interlocked.Increment(ref owner.StatusCalls);
            return Task.FromResult(owner.States.GetValueOrDefault(reference) ?? new OperatorPaymentStatus(OperatorPaymentState.Pending));
        }

        public Task TestConnectionAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
