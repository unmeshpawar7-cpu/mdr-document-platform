# ADR 0001: In-process Channel queue for upload processing

* Status: Accepted
* Date: 2026-10-08

## Context
CSV validation of a 50,000-row MDR takes seconds to minutes; it must not run on the request thread. Options: an
in-process `System.Threading.Channels` queue with a `BackgroundService`, Hangfire (SQL Server storage), or a broker
(Azure Service Bus / RabbitMQ) with separate workers.

## Decision
Use a bounded `Channel<Guid>` plus `UploadProcessingWorker`. The database row (`MdrUpload.Status`) is the durable
record of work; the channel only carries ids. On startup, `Queued`/`Processing` uploads are re-enqueued and partial
results of interrupted attempts are deleted, which makes processing idempotent. Parallelism and capacity are options.

## Consequences
* No extra infrastructure, no serialisation of jobs, trivially testable with fakes.
* Work is bound to the API process: horizontal scale-out needs either leader election or a move to a broker.
  The `IUploadQueue` port keeps that swap local to Infrastructure.
* No dashboard or scheduled retries as Hangfire would give; retries are bounded by `MaxProcessingAttempts`.
* An abandoned enqueue (client gone while the queue is full) waits for the next restart - acceptable at current scale,
  documented as a known limitation.
