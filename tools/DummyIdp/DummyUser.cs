namespace DummyIdp;

/// <summary>One selectable test identity. The values map directly onto the custom access-token claims.</summary>
public sealed class DummyUser
{
    /// <summary>Access-token <c>sub</c> claim (shaped like <c>first.lastName</c>).</summary>
    public string Sub { get; set; } = "";

    /// <summary>Access-token <c>SAMAccount</c> claim (email / login id).</summary>
    public string SamAccount { get; set; } = "";

    /// <summary>Access-token <c>empID</c> claim (AD employee id).</summary>
    public string EmpId { get; set; } = "";

    /// <summary>Friendly label for the sign-in page.</summary>
    public string DisplayName { get; set; } = "";
}
