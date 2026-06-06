namespace OutWit.Database.Native;

internal enum WitDbStatusCode : uint
{
    Ok = 0,
    InvalidArgument = 1,
    NotFound = 2,
    PasswordRequired = 3,
    WrongPassword = 4,
    ConfigMismatch = 5,
    UnknownProvider = 6,
    TxnNotSupported = 7,
    TxnActive = 8,
    StoreError = 9,
    InvalidHandle = 10,
}
