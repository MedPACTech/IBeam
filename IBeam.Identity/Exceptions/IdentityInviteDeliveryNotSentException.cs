namespace IBeam.Identity.Exceptions;
/// <summary>Message preparation failed before invoking the communication sender. Contains no recipient or provider detail.</summary>
public sealed class IdentityInviteDeliveryNotSentException() : Exception("Invitation delivery was not attempted.");
