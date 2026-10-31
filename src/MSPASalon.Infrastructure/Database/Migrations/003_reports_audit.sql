-- M4a (BUSINESS_RULES v0.5, T1): data needed by the reports and the audit trail.

-- Material cost of each service line at posting time (recipe qty x product cost price), K21 / §14.
-- NULL = posted before this migration: reports estimate it from the current recipe and mark the row.
ALTER TABLE treatment_line ADD COLUMN material_cost INTEGER CHECK (material_cost IS NULL OR material_cost >= 0);

-- Who cancelled a document, and when (Laporan Pembatalan).
ALTER TABLE registration     ADD COLUMN cancelled_by TEXT;
ALTER TABLE registration     ADD COLUMN cancelled_at TEXT;
ALTER TABLE treatment        ADD COLUMN cancelled_by TEXT;
ALTER TABLE treatment        ADD COLUMN cancelled_at TEXT;
ALTER TABLE sale             ADD COLUMN cancelled_by TEXT;
ALTER TABLE sale             ADD COLUMN cancelled_at TEXT;
ALTER TABLE purchase         ADD COLUMN cancelled_by TEXT;
ALTER TABLE purchase         ADD COLUMN cancelled_at TEXT;
ALTER TABLE stock_adjustment ADD COLUMN cancelled_by TEXT;
ALTER TABLE stock_adjustment ADD COLUMN cancelled_at TEXT;

-- Permanent audit trail: cancellations, data reset, restore. Never deleted by the transaction reset (§16).
CREATE TABLE audit_log (
    id        INTEGER PRIMARY KEY,
    at        TEXT NOT NULL,
    user_name TEXT NOT NULL,
    action    TEXT NOT NULL,              -- CANCEL, RESET, RESTORE
    doc_type  TEXT,                       -- REGISTRATION, TREATMENT, SALE, PURCHASE, ADJUSTMENT
    doc_id    INTEGER,
    doc_no    TEXT,
    details   TEXT
);
CREATE INDEX ix_audit_log_at ON audit_log(at);

-- Report queries filter by posting / payment time.
CREATE INDEX ix_payment_paid_at  ON payment(paid_at);
CREATE INDEX ix_sale_posted      ON sale(posted_at);
CREATE INDEX ix_sale_customer    ON sale(customer_id);
CREATE INDEX ix_treatment_line_treatment ON treatment_line(treatment_id);
