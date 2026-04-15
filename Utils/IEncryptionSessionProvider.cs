namespace ReplayLogger
{
    internal interface IEncryptionSessionProvider
    {
        KeyloggerLogEncryption.Session EncryptionSession { get; }
    }
}
