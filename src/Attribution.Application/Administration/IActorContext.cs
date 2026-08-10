namespace Attribution.Application.Administration;

// Who is making the current request — abstracted so Application services never take a
// direct dependency on HttpContext (Constitution Principle II: Domain/Application stay
// framework-free).
public interface IActorContext
{
    string? ActorUserId { get; }
}
