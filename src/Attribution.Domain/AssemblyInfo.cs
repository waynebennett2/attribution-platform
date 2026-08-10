using System.Runtime.CompilerServices;

// Infrastructure needs to rehydrate entities from database rows via internal factory
// methods, without exposing public mutation surface to Application/Api (see e.g.
// Website.Rehydrate). UnitTests gets the same visibility for white-box entity tests.
[assembly: InternalsVisibleTo("Attribution.Infrastructure")]
[assembly: InternalsVisibleTo("Attribution.UnitTests")]
