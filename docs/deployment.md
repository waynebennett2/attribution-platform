# Deployment topology (FR-043, T119)

FR-043: "System MUST scale horizontally as call and traffic volume grows, with no single
point of failure in the visitor-facing allocation path." `docker-compose.yml` provisions
the minimal topology that satisfies this locally:

```
                    ┌─────────┐
  visitor traffic → │  nginx  │ (nginx.conf: passive health check, retry-next-upstream)
                    └────┬────┘
                 ┌───────┴───────┐
                 ▼               ▼
             ┌───────┐       ┌───────┐
             │ api1  │       │ api2  │   (Attribution.Api — stateless, N-way active-active)
             └───┬───┘       └───┬───┘
                 └───────┬───────┘
                          ▼
                    ┌──────────┐
                    │  mysql   │ ← the atomic allocator's FOR UPDATE SKIP LOCKED query
                    └──────────┘   (research.md §2) is what makes concurrent api1/api2
                          ▲          allocation requests safe against each other
                 ┌────────┴────────┐
                 │                 │
            ┌─────────┐      ┌─────────┐
            │ worker1 │      │ worker2 │   (Attribution.Workers — active/cold-standby;
            │ active  │      │ standby │    see docker-compose.yml's own caveat comment)
            └─────────┘      └─────────┘
```

## Running it

```
cp .env.example .env
# edit .env: JWT_SIGNING_SECRET and RETENTION_HMAC_KEY, each >= 32 characters
docker compose up --build
```

`nginx` publishes port 8080; `mysql` still publishes 3306 directly for local tooling
(`mysql` CLI, the seed/schema scripts) exactly as it did before this topology existed.

## Why the Api tier is safely N-way active-active

Every allocation request is independent and stateless at the application layer — nothing
in `Attribution.Api` holds in-process state across requests. The one place concurrent
requests genuinely contend (two visitors racing for the same tracking number) is resolved
in the database itself, via `AtomicAllocator`'s `FOR UPDATE SKIP LOCKED` query
(research.md §2), not by anything the Api instances coordinate between themselves. This is
what makes adding a third, fourth, or Nth `api` replica just a matter of adding another
service block — no new coordination is needed as the fleet grows.

## Failover (SC-005)

`nginx.conf`'s `upstream` block marks a replica failed after 2 failed requests within 10s
(`max_fails`/`fail_timeout`) and stops routing to it for that window; `proxy_next_upstream`
additionally retries a single failed request against the other replica immediately, rather
than surfacing the failure to the visitor — this is what lets a mid-flight instance loss
produce zero failed allocation requests (SC-005), not merely a shorter outage. This is
nginx open source's *passive* health check; an *active* prober against `GET /health`
(nginx-plus, Traefik, HAProxy, or a cloud load balancer's own health-check feature) is a
reasonable upgrade for a real deployment, catching a dead replica before the first request
hits it rather than after — not required to satisfy FR-043/SC-005's bar, which is about
surviving one replica's failure, not preventing the first failed request from ever
occurring.

## Known limitations of this topology

- **Workers is not yet verified safe as N-way active-active.** `IngestionWorker`'s
  checkpoint advance and `PublicationWorker`'s outbox drain both read-then-write with no
  distributed lock; two simultaneously active replicas racing the same feed/outbox is
  unverified. `worker2` is provisioned as a stopped, cold-standby service specifically so
  it exists (`docker compose start worker2`) without running live traffic through an
  uncoordinated second copy. Making Workers genuinely N-way active-active (e.g. an
  advisory-lock-per-feed, or partitioning work by pool/destination) is a documented
  follow-up, not something this increment's acceptance scenarios require.
- **MySQL itself is a single point of failure in this compose file.** FR-043's "no single
  point of failure" is scoped to "the visitor-facing allocation path" — the Api/nginx tier
  above — not the database tier, which this topology doesn't attempt to make redundant
  (MySQL replication, a managed HA offering, etc. are a deployment-time choice outside this
  repository's scope).
