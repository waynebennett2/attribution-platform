using Attribution.Domain.Identity;

namespace Attribution.Infrastructure.Identity;

public interface ITokenIssuer
{
    string IssueToken(User user, DateTimeOffset issuedAt);
}
