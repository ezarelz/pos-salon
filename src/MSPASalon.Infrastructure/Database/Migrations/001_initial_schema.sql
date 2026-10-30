-- =====================================================================================
-- MSPA Salon - Migration 001: initial schema (all modules).
--
-- Conventions
--   * Money        : INTEGER whole Rupiah                         (columns: price, amount, ...)
--   * Quantity     : INTEGER scaled x 10,000 (4 decimals)         (columns ending in _x)
--   * Date/time    : TEXT 'yyyy-MM-dd HH:mm:ss' local time
--   * Booleans     : INTEGER 0/1 with CHECK
--   * Masters may be deleted (decision K10). Transactions keep a snapshot of code/name/price,
--     so their FK to a master is ON DELETE SET NULL and history stays readable.
--   * The stock ledger is the source of truth for stock; product.stock_on_hand_x is a cache.
-- =====================================================================================

-- ---------------------------------------------------------------- settings & security
CREATE TABLE app_setting (
    key         TEXT PRIMARY KEY,
    value       TEXT NOT NULL
);

CREATE TABLE branch (
    id              INTEGER PRIMARY KEY CHECK (id = 1),           -- single salon (single PC, K5)
    short_name      TEXT NOT NULL DEFAULT '',
    name            TEXT NOT NULL,
    address         TEXT,
    phone           TEXT,
    receipt_footer  TEXT,
    paper_width_mm  INTEGER NOT NULL DEFAULT 58 CHECK (paper_width_mm IN (58, 80))
);

CREATE TABLE permission (
    code        TEXT PRIMARY KEY COLLATE NOCASE,
    name        TEXT NOT NULL,
    module      TEXT NOT NULL
);

CREATE TABLE role (
    id          INTEGER PRIMARY KEY,
    code        TEXT NOT NULL UNIQUE COLLATE NOCASE,
    name        TEXT NOT NULL,
    is_system   INTEGER NOT NULL DEFAULT 0 CHECK (is_system IN (0, 1))
);

CREATE TABLE role_permission (
    role_id          INTEGER NOT NULL REFERENCES role(id) ON DELETE CASCADE,
    permission_code  TEXT NOT NULL REFERENCES permission(code) ON DELETE CASCADE,
    PRIMARY KEY (role_id, permission_code)
);

CREATE TABLE employee (
    id          INTEGER PRIMARY KEY,
    code        TEXT NOT NULL UNIQUE COLLATE NOCASE,
    name        TEXT NOT NULL,
    gender      TEXT CHECK (gender IN ('L', 'P')),
    position    TEXT,
    phone       TEXT,
    address     TEXT,
    is_capster  INTEGER NOT NULL DEFAULT 0 CHECK (is_capster IN (0, 1)),
    is_active   INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0, 1)),
    created_at  TEXT NOT NULL,
    updated_at  TEXT NOT NULL
);

CREATE TABLE app_user (
    id                    INTEGER PRIMARY KEY,
    username              TEXT NOT NULL UNIQUE COLLATE NOCASE,
    display_name          TEXT NOT NULL,
    employee_id           INTEGER REFERENCES employee(id) ON DELETE SET NULL,
    role_id               INTEGER NOT NULL REFERENCES role(id) ON DELETE RESTRICT,
    password_hash         TEXT NOT NULL,
    password_salt         TEXT NOT NULL,
    password_iterations   INTEGER NOT NULL CHECK (password_iterations > 0),
    is_active             INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0, 1)),
    must_change_password  INTEGER NOT NULL DEFAULT 0 CHECK (must_change_password IN (0, 1)),
    created_at            TEXT NOT NULL,
    updated_at            TEXT NOT NULL
);

-- ---------------------------------------------------------------- master data
CREATE TABLE customer (
    id          INTEGER PRIMARY KEY,
    code        TEXT NOT NULL UNIQUE COLLATE NOCASE,
    name        TEXT NOT NULL,
    phone       TEXT,
    address     TEXT,
    gender      TEXT CHECK (gender IN ('L', 'P')),
    is_member   INTEGER NOT NULL DEFAULT 0 CHECK (is_member IN (0, 1)),
    is_walk_in  INTEGER NOT NULL DEFAULT 0 CHECK (is_walk_in IN (0, 1)),
    is_active   INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0, 1)),
    notes       TEXT,
    created_at  TEXT NOT NULL,
    updated_at  TEXT NOT NULL
);

CREATE TABLE supplier (
    id              INTEGER PRIMARY KEY,
    code            TEXT NOT NULL UNIQUE COLLATE NOCASE,
    name            TEXT NOT NULL,
    phone           TEXT,
    address         TEXT,
    contact_person  TEXT,
    is_active       INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0, 1)),
    created_at      TEXT NOT NULL,
    updated_at      TEXT NOT NULL
);

CREATE TABLE category (
    id          INTEGER PRIMARY KEY,
    code        TEXT NOT NULL UNIQUE COLLATE NOCASE,
    name        TEXT NOT NULL
);

CREATE TABLE product (
    id               INTEGER PRIMARY KEY,
    code             TEXT NOT NULL UNIQUE COLLATE NOCASE,
    name             TEXT NOT NULL,
    category_id      INTEGER REFERENCES category(id) ON DELETE SET NULL,
    unit             TEXT NOT NULL,
    content_qty_x    INTEGER CHECK (content_qty_x IS NULL OR content_qty_x > 0),
    content_unit     TEXT,
    cost_price       INTEGER NOT NULL DEFAULT 0 CHECK (cost_price >= 0),
    sell_price       INTEGER NOT NULL DEFAULT 0 CHECK (sell_price >= 0),
    stock_min_x      INTEGER NOT NULL DEFAULT 0 CHECK (stock_min_x >= 0),
    stock_max_x      INTEGER NOT NULL DEFAULT 0 CHECK (stock_max_x >= 0),
    is_sellable      INTEGER NOT NULL DEFAULT 1 CHECK (is_sellable IN (0, 1)),
    is_consumable    INTEGER NOT NULL DEFAULT 0 CHECK (is_consumable IN (0, 1)),
    is_active        INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0, 1)),
    stock_on_hand_x  INTEGER NOT NULL DEFAULT 0,                   -- cache of SUM(stock_ledger); may be negative (A4)
    created_at       TEXT NOT NULL,
    updated_at       TEXT NOT NULL,
    CHECK (is_sellable = 1 OR is_consumable = 1),
    CHECK (stock_max_x = 0 OR stock_max_x >= stock_min_x)
);

CREATE TABLE service (
    id          INTEGER PRIMARY KEY,
    code        TEXT NOT NULL UNIQUE COLLATE NOCASE,
    name        TEXT NOT NULL,
    price       INTEGER NOT NULL CHECK (price >= 0),
    commission  INTEGER NOT NULL DEFAULT 0 CHECK (commission >= 0),
    is_active   INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0, 1)),
    created_at  TEXT NOT NULL,
    updated_at  TEXT NOT NULL,
    CHECK (commission <= price)
);

-- Recipe (BOM): consumption per 1 customer, in the product's stock unit (K9).
CREATE TABLE service_material (
    id          INTEGER PRIMARY KEY,
    service_id  INTEGER NOT NULL REFERENCES service(id) ON DELETE CASCADE,
    product_id  INTEGER NOT NULL REFERENCES product(id) ON DELETE CASCADE,
    qty_x       INTEGER NOT NULL CHECK (qty_x > 0),
    UNIQUE (service_id, product_id)
);

CREATE TABLE payment_method (
    id          INTEGER PRIMARY KEY,
    code        TEXT NOT NULL UNIQUE COLLATE NOCASE,
    name        TEXT NOT NULL,
    is_cash     INTEGER NOT NULL DEFAULT 0 CHECK (is_cash IN (0, 1)),
    is_active   INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0, 1)),
    sort_order  INTEGER NOT NULL DEFAULT 0
);

-- ---------------------------------------------------------------- numbering
-- One row per document type per month. Incremented with an atomic UPSERT ... RETURNING
-- inside the SAME transaction that saves the document, so a failed save never consumes a number.
CREATE TABLE doc_counter (
    doc_type    TEXT NOT NULL,
    period      TEXT NOT NULL,                                     -- 'yyMM'
    last_no     INTEGER NOT NULL CHECK (last_no > 0),
    PRIMARY KEY (doc_type, period)
);

-- ---------------------------------------------------------------- registration -> treatment (K1)
CREATE TABLE registration (
    id              INTEGER PRIMARY KEY,
    reg_no          TEXT NOT NULL UNIQUE,
    reg_at          TEXT NOT NULL,
    customer_id     INTEGER REFERENCES customer(id) ON DELETE SET NULL,
    customer_code   TEXT NOT NULL,
    customer_name   TEXT NOT NULL,
    notes           TEXT,
    status          TEXT NOT NULL CHECK (status IN ('MENUNGGU', 'DIPROSES', 'SELESAI', 'BATAL')),
    cancel_reason   TEXT,
    created_by      TEXT NOT NULL,
    created_at      TEXT NOT NULL,
    updated_at      TEXT NOT NULL,
    CHECK (status <> 'BATAL' OR length(trim(coalesce(cancel_reason, ''))) > 0)
);

CREATE TABLE registration_line (
    id               INTEGER PRIMARY KEY,
    registration_id  INTEGER NOT NULL REFERENCES registration(id) ON DELETE CASCADE,
    line_no          INTEGER NOT NULL CHECK (line_no > 0),
    service_id       INTEGER REFERENCES service(id) ON DELETE SET NULL,
    service_code     TEXT NOT NULL,
    service_name     TEXT NOT NULL,
    price            INTEGER NOT NULL CHECK (price >= 0),
    employee_id      INTEGER REFERENCES employee(id) ON DELETE SET NULL,
    employee_code    TEXT NOT NULL,
    employee_name    TEXT NOT NULL,
    UNIQUE (registration_id, line_no)
);

-- One registration -> at most one treatment (UNIQUE registration_id). doc_no = reg_no (rule P1).
CREATE TABLE treatment (
    id               INTEGER PRIMARY KEY,
    registration_id  INTEGER NOT NULL UNIQUE REFERENCES registration(id) ON DELETE RESTRICT,
    doc_no           TEXT NOT NULL UNIQUE,
    time_in          TEXT NOT NULL,
    time_out         TEXT,
    status           TEXT NOT NULL CHECK (status IN ('DIKERJAKAN', 'LUNAS', 'BATAL')),
    subtotal         INTEGER NOT NULL DEFAULT 0 CHECK (subtotal >= 0),
    discount         INTEGER NOT NULL DEFAULT 0 CHECK (discount >= 0),
    grand_total      INTEGER NOT NULL DEFAULT 0,
    cancel_reason    TEXT,
    posted_by        TEXT,
    posted_at        TEXT,
    created_by       TEXT NOT NULL,
    created_at       TEXT NOT NULL,
    updated_at       TEXT NOT NULL,
    CHECK (discount <= subtotal),
    CHECK (grand_total = subtotal - discount),
    CHECK (status <> 'LUNAS' OR (time_out IS NOT NULL AND posted_at IS NOT NULL)),
    CHECK (status <> 'BATAL' OR length(trim(coalesce(cancel_reason, ''))) > 0)
);

CREATE TABLE treatment_line (
    id              INTEGER PRIMARY KEY,
    treatment_id    INTEGER NOT NULL REFERENCES treatment(id) ON DELETE CASCADE,
    line_no         INTEGER NOT NULL CHECK (line_no > 0),
    service_id      INTEGER REFERENCES service(id) ON DELETE SET NULL,
    service_code    TEXT NOT NULL,
    service_name    TEXT NOT NULL,
    price           INTEGER NOT NULL CHECK (price >= 0),
    employee_id     INTEGER REFERENCES employee(id) ON DELETE SET NULL,
    employee_code   TEXT NOT NULL,
    employee_name   TEXT NOT NULL,
    commission      INTEGER NOT NULL DEFAULT 0 CHECK (commission >= 0),   -- snapshot at posting (§11)
    UNIQUE (treatment_id, line_no)
);

-- ---------------------------------------------------------------- direct product sale
CREATE TABLE sale (
    id              INTEGER PRIMARY KEY,
    doc_no          TEXT NOT NULL UNIQUE,
    doc_at          TEXT NOT NULL,
    customer_id     INTEGER REFERENCES customer(id) ON DELETE SET NULL,
    customer_code   TEXT NOT NULL,
    customer_name   TEXT NOT NULL,
    status          TEXT NOT NULL CHECK (status IN ('DRAFT', 'POSTED', 'BATAL')),
    subtotal        INTEGER NOT NULL DEFAULT 0 CHECK (subtotal >= 0),
    discount_total  INTEGER NOT NULL DEFAULT 0 CHECK (discount_total >= 0),
    grand_total     INTEGER NOT NULL DEFAULT 0 CHECK (grand_total >= 0),
    notes           TEXT,
    cancel_reason   TEXT,
    posted_by       TEXT,
    posted_at       TEXT,
    created_by      TEXT NOT NULL,
    created_at      TEXT NOT NULL,
    updated_at      TEXT NOT NULL,
    CHECK (grand_total = subtotal - discount_total),
    CHECK (status <> 'BATAL' OR length(trim(coalesce(cancel_reason, ''))) > 0)
);

CREATE TABLE sale_line (
    id            INTEGER PRIMARY KEY,
    sale_id       INTEGER NOT NULL REFERENCES sale(id) ON DELETE CASCADE,
    line_no       INTEGER NOT NULL CHECK (line_no > 0),
    product_id    INTEGER REFERENCES product(id) ON DELETE SET NULL,
    product_code  TEXT NOT NULL,
    product_name  TEXT NOT NULL,
    unit          TEXT NOT NULL,
    qty_x         INTEGER NOT NULL CHECK (qty_x > 0),
    price         INTEGER NOT NULL CHECK (price >= 0),
    gross         INTEGER NOT NULL CHECK (gross >= 0),               -- round(qty x price)
    discount      INTEGER NOT NULL DEFAULT 0 CHECK (discount >= 0),
    line_total    INTEGER NOT NULL CHECK (line_total >= 0),
    cost_price    INTEGER NOT NULL DEFAULT 0 CHECK (cost_price >= 0),
    UNIQUE (sale_id, line_no),
    CHECK (discount <= gross),
    CHECK (line_total = gross - discount)
);

-- ---------------------------------------------------------------- purchasing
CREATE TABLE purchase (
    id              INTEGER PRIMARY KEY,
    doc_no          TEXT NOT NULL UNIQUE,
    doc_at          TEXT NOT NULL,
    supplier_id     INTEGER REFERENCES supplier(id) ON DELETE SET NULL,
    supplier_code   TEXT NOT NULL,
    supplier_name   TEXT NOT NULL,
    supplier_ref    TEXT,                                          -- supplier invoice no.
    status          TEXT NOT NULL CHECK (status IN ('DRAFT', 'POSTED', 'BATAL')),
    grand_total     INTEGER NOT NULL DEFAULT 0 CHECK (grand_total >= 0),
    notes           TEXT,
    cancel_reason   TEXT,
    posted_by       TEXT,
    posted_at       TEXT,
    created_by      TEXT NOT NULL,
    created_at      TEXT NOT NULL,
    updated_at      TEXT NOT NULL,
    CHECK (status <> 'BATAL' OR length(trim(coalesce(cancel_reason, ''))) > 0)
);

CREATE TABLE purchase_line (
    id            INTEGER PRIMARY KEY,
    purchase_id   INTEGER NOT NULL REFERENCES purchase(id) ON DELETE CASCADE,
    line_no       INTEGER NOT NULL CHECK (line_no > 0),
    product_id    INTEGER REFERENCES product(id) ON DELETE SET NULL,
    product_code  TEXT NOT NULL,
    product_name  TEXT NOT NULL,
    unit          TEXT NOT NULL,
    qty_x         INTEGER NOT NULL CHECK (qty_x > 0),
    price         INTEGER NOT NULL CHECK (price >= 0),
    line_total    INTEGER NOT NULL CHECK (line_total >= 0),
    UNIQUE (purchase_id, line_no)
);

-- ---------------------------------------------------------------- stock adjustment / opname
CREATE TABLE stock_adjustment (
    id              INTEGER PRIMARY KEY,
    doc_no          TEXT NOT NULL UNIQUE,
    doc_at          TEXT NOT NULL,
    reason          TEXT NOT NULL CHECK (length(trim(reason)) > 0),
    status          TEXT NOT NULL CHECK (status IN ('DRAFT', 'POSTED', 'BATAL')),
    total_value     INTEGER NOT NULL DEFAULT 0,                    -- valued at cost (§8), may be negative
    cancel_reason   TEXT,
    posted_by       TEXT,
    posted_at       TEXT,
    created_by      TEXT NOT NULL,
    created_at      TEXT NOT NULL,
    updated_at      TEXT NOT NULL,
    CHECK (status <> 'BATAL' OR length(trim(coalesce(cancel_reason, ''))) > 0)
);

CREATE TABLE stock_adjustment_line (
    id              INTEGER PRIMARY KEY,
    adjustment_id   INTEGER NOT NULL REFERENCES stock_adjustment(id) ON DELETE CASCADE,
    line_no         INTEGER NOT NULL CHECK (line_no > 0),
    product_id      INTEGER REFERENCES product(id) ON DELETE SET NULL,
    product_code    TEXT NOT NULL,
    product_name    TEXT NOT NULL,
    unit            TEXT NOT NULL,
    qty_x           INTEGER NOT NULL CHECK (qty_x <> 0),           -- signed: + in, - out
    cost_price      INTEGER NOT NULL DEFAULT 0 CHECK (cost_price >= 0),
    line_value      INTEGER NOT NULL,
    UNIQUE (adjustment_id, line_no)
);

-- ---------------------------------------------------------------- payment (K2, K8: recording only)
CREATE TABLE payment (
    id                 INTEGER PRIMARY KEY,
    source_type        TEXT NOT NULL CHECK (source_type IN ('SALE', 'TREATMENT')),
    source_id          INTEGER NOT NULL,
    payment_method_id  INTEGER REFERENCES payment_method(id) ON DELETE SET NULL,
    method_code        TEXT NOT NULL,
    method_name        TEXT NOT NULL,
    is_cash            INTEGER NOT NULL CHECK (is_cash IN (0, 1)),
    amount_due         INTEGER NOT NULL CHECK (amount_due >= 0),
    amount_received    INTEGER NOT NULL,
    change_amount      INTEGER NOT NULL DEFAULT 0 CHECK (change_amount >= 0),
    reference          TEXT,
    paid_at            TEXT NOT NULL,
    created_by         TEXT NOT NULL,
    UNIQUE (source_type, source_id),                               -- one payment per document (Y2)
    CHECK (amount_received >= amount_due),                         -- no underpayment (Y3)
    CHECK (change_amount = amount_received - amount_due),
    CHECK (is_cash = 1 OR change_amount = 0)                       -- non-cash: exact amount (Y4)
);

-- ---------------------------------------------------------------- stock ledger (source of truth, §10)
CREATE TABLE stock_ledger (
    id              INTEGER PRIMARY KEY,
    product_id      INTEGER NOT NULL REFERENCES product(id) ON DELETE CASCADE,
    moved_at        TEXT NOT NULL,
    source_type     TEXT NOT NULL CHECK (source_type IN ('OPENING', 'PURCHASE', 'SALE', 'TREATMENT', 'ADJUSTMENT')),
    source_id       INTEGER NOT NULL,
    source_line_id  INTEGER NOT NULL DEFAULT 0,                    -- 0 when aggregated (treatment backflush per product)
    doc_no          TEXT NOT NULL,
    qty_in_x        INTEGER NOT NULL DEFAULT 0 CHECK (qty_in_x >= 0),
    qty_out_x       INTEGER NOT NULL DEFAULT 0 CHECK (qty_out_x >= 0),
    unit_cost       INTEGER NOT NULL DEFAULT 0 CHECK (unit_cost >= 0),
    notes           TEXT,
    CHECK ((qty_in_x > 0 AND qty_out_x = 0) OR (qty_in_x = 0 AND qty_out_x > 0)),
    UNIQUE (source_type, source_id, source_line_id, product_id)    -- prevents double posting (B6)
);

-- ---------------------------------------------------------------- indexes
CREATE INDEX ix_product_category        ON product(category_id);
CREATE INDEX ix_service_material_prod   ON service_material(product_id);
CREATE INDEX ix_app_user_role           ON app_user(role_id);
CREATE INDEX ix_registration_status     ON registration(status, reg_at);
CREATE INDEX ix_registration_customer   ON registration(customer_id);
CREATE INDEX ix_registration_line_emp   ON registration_line(employee_id);
CREATE INDEX ix_treatment_status        ON treatment(status, time_in);
CREATE INDEX ix_treatment_posted        ON treatment(posted_at);
CREATE INDEX ix_treatment_line_emp      ON treatment_line(employee_id);
CREATE INDEX ix_sale_doc_at             ON sale(doc_at);
CREATE INDEX ix_sale_status             ON sale(status);
CREATE INDEX ix_sale_line_product       ON sale_line(product_id);
CREATE INDEX ix_purchase_doc_at         ON purchase(doc_at);
CREATE INDEX ix_purchase_line_product   ON purchase_line(product_id);
CREATE INDEX ix_adjustment_doc_at       ON stock_adjustment(doc_at);
CREATE INDEX ix_payment_paid_at         ON payment(paid_at);
CREATE INDEX ix_stock_ledger_product    ON stock_ledger(product_id, moved_at);
CREATE INDEX ix_stock_ledger_source     ON stock_ledger(source_type, source_id);
