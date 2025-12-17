using System;
using System.Text.Json;
using WebhookDelivery.Core.Models;
using Xunit;

namespace WebhookDelivery.UnitTests;

public class ModelsTests
{
    [Fact]
    public void Saga_IsTerminal_Works()
    {
        Assert.False(new WebhookDeliverySaga { Status = SagaStatus.Pending }.IsTerminal());
        Assert.False(new WebhookDeliverySaga { Status = SagaStatus.InProgress }.IsTerminal());
        Assert.False(new WebhookDeliverySaga { Status = SagaStatus.PendingRetry }.IsTerminal());
        Assert.True(new WebhookDeliverySaga { Status = SagaStatus.Completed }.IsTerminal());
        Assert.True(new WebhookDeliverySaga { Status = SagaStatus.DeadLettered }.IsTerminal());
    }

    [Fact]
    public void Job_IsActiveAndTerminal_Works()
    {
        Assert.True(new WebhookDeliveryJob { Status = JobStatus.Pending }.IsActive());
        Assert.True(new WebhookDeliveryJob { Status = JobStatus.Leased }.IsActive());
        Assert.False(new WebhookDeliveryJob { Status = JobStatus.Completed }.IsActive());
        Assert.False(new WebhookDeliveryJob { Status = JobStatus.Failed }.IsActive());

        Assert.False(new WebhookDeliveryJob { Status = JobStatus.Pending }.IsTerminal());
        Assert.False(new WebhookDeliveryJob { Status = JobStatus.Leased }.IsTerminal());
        Assert.True(new WebhookDeliveryJob { Status = JobStatus.Completed }.IsTerminal());
        Assert.True(new WebhookDeliveryJob { Status = JobStatus.Failed }.IsTerminal());
    }

    [Fact]
    public void DeadLetter_FromSaga_RequiresDeadLetteredTerminalSaga()
    {
        var payload = JsonDocument.Parse("{\"ok\":true}");

        Assert.Throws<InvalidOperationException>(() =>
            DeadLetter.FromSaga(new WebhookDeliverySaga { Status = SagaStatus.Pending }, payload));

        Assert.Throws<InvalidOperationException>(() =>
            DeadLetter.FromSaga(new WebhookDeliverySaga { Status = SagaStatus.Completed }, payload));

        var saga = new WebhookDeliverySaga
        {
            Id = 123,
            EventId = 10,
            SubscriptionId = 20,
            Status = SagaStatus.DeadLettered,
            FinalErrorCode = "HTTP_500"
        };

        var dl = DeadLetter.FromSaga(saga, payload);
        Assert.Equal(123, dl.SagaId);
        Assert.Equal(10, dl.EventId);
        Assert.Equal(20, dl.SubscriptionId);
        Assert.Equal("HTTP_500", dl.FinalErrorCode);
        Assert.True(dl.PayloadSnapshot.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean());
    }
}
