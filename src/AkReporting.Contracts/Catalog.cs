using System;

namespace AkReporting.Contracts
{
    public sealed class DoctorVersion
    {
        public Guid Id { get; set; }
        public Guid DoctorId { get; set; }
        public int Version { get; set; }
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Qualification { get; set; } = "";
        public string Designation { get; set; } = "";
        public string RegistrationNumber { get; set; } = "";
        public string Specialty { get; set; } = "";
        public string PermissionEvidence { get; set; } = "";
        public byte[]? SignaturePng { get; set; }
        public byte[]? StampPng { get; set; }
        public bool Active { get; set; } = true;
    }
    public sealed class LoginRequest
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }
    public sealed class LoginResponse
    {
        public string Token { get; set; } = "";
        public DateTimeOffset ExpiresAt { get; set; }
        public string Role { get; set; } = "";
    }
    public sealed class CreateUserRequest
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string Role { get; set; } = "Writer";
    }
    public sealed class ApprovalRequest
    {
        public string ReviewerName { get; set; } = "";
        public string Evidence { get; set; } = "";
    }
    public sealed class CenterSettings
    {
        public string CenterName { get; set; } = "A K Diagnostic Centre & Polyclinic";
        public string ReportPrefix { get; set; } = "AKDC";
        public int RetentionDays { get; set; } = 365;
    }
}
