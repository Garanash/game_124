-- Login credentials; gameplay data lives in grain_state under the AccountGrain with the same id.
CREATE TABLE IF NOT EXISTS accounts (
    id            uuid PRIMARY KEY,
    email         text NOT NULL UNIQUE,
    password_hash text NOT NULL,
    created_at    timestamptz NOT NULL DEFAULT now()
);

-- Orleans grain state as JSONB; version is the optimistic-concurrency ETag.
CREATE TABLE IF NOT EXISTS grain_state (
    grain_id   text NOT NULL,
    state_name text NOT NULL,
    payload    jsonb NOT NULL,
    version    integer NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (grain_id, state_name)
);

-- Store receipts are kept forever for refunds and chargeback disputes.
CREATE TABLE IF NOT EXISTS store_receipts (
    platform       text NOT NULL,
    transaction_id text NOT NULL,
    account_id     uuid NOT NULL,
    sku_id         text NOT NULL,
    status         text NOT NULL,
    raw_payload    jsonb,
    received_at    timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (platform, transaction_id)
);

CREATE INDEX IF NOT EXISTS store_receipts_account ON store_receipts (account_id);
