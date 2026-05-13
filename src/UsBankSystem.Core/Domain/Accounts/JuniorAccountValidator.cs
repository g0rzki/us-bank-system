namespace UsBankSystem.Core.Domain.Accounts;

public static class JuniorAccountValidator
{
    private const int MinAge = 7;
    private const int MaxAge = 13;

    public static void ValidateAge(DateOnly dateOfBirth)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var age = today.Year - dateOfBirth.Year;
        if (dateOfBirth > today.AddYears(-age)) age--;

        if (age < MinAge || age > MaxAge)
            throw new ArgumentException($"Junior account holder must be between {MinAge} and {MaxAge} years old. Provided age: {age}");
    }

    public static void ValidateDateOfBirth(DateOnly dateOfBirth)
    {
        if (dateOfBirth >= DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ArgumentException("Date of birth cannot be in the future");

        ValidateAge(dateOfBirth);
    }
}