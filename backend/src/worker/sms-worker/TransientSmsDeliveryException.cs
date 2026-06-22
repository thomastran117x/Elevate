namespace backend.worker.sms_worker;

public sealed class TransientSmsDeliveryException : Exception
{
    public TransientSmsDeliveryException(string message)
        : base(message)
    {
    }

    public TransientSmsDeliveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
