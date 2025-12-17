# Database Permissions Matrix (PostgreSQL)

This project uses PostgreSQL roles to enforce separation of concerns.

## Roles (summary)

| Role | events | subscriptions | webhook_delivery_sagas | webhook_delivery_jobs | dead_letters | router_offsets |
| --- | --- | --- | --- | --- | --- | --- |
| `event_ingest_writer` | SELECT, INSERT | SELECT | - | - | - | - |
| `router_worker` | SELECT | SELECT | SELECT, INSERT | - | - | SELECT, INSERT, UPDATE |
| `saga_orchestrator` | SELECT | SELECT | SELECT, UPDATE | SELECT, INSERT, UPDATE | SELECT, INSERT | - |
| `job_worker` | SELECT | SELECT | SELECT | SELECT, UPDATE | - | - |
| `dead_letter_operator` | SELECT | SELECT | SELECT, INSERT | - | SELECT, INSERT | - |
| `subscription_admin` | - | SELECT, INSERT, UPDATE | - | - | - | - |

## Notes
- Only `saga_orchestrator` is allowed to transition saga state (`UPDATE webhook_delivery_sagas`).
- `router_worker` is allowed to insert sagas and update `router_offsets` only.
- `job_worker` must never modify sagas; it reports results by updating jobs.

## Setup scripts
- Development roles: `src/WebhookDelivery.Database/Scripts/002_DatabaseRoles_Development.sql`
- Production template: `src/WebhookDelivery.Database/Templates/002_DatabaseRoles_Production_Template.sql`
