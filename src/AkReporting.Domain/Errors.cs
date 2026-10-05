namespace AkReporting.Domain;

public sealed class ValidationException(string message) : Exception(message);
public sealed class ConflictException(string message) : Exception(message);
public sealed class NotFoundException(string message) : Exception(message);
public sealed class IntegrityException(string message) : Exception(message);

public sealed record Actor(Guid Id, string Role)
{
    public bool CanWrite => Role is "Writer" or "MedicalReviewer";
    public bool CanCreateCases => CanWrite || Role is "Receptionist";
}
