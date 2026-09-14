-- Fase 1.1 - Módulo de paletización y recibo digital
-- Tabla de cabecera: un renglón por recibo/PO en proceso de paletización.
-- Convenciones tomadas de las tablas existentes de ZPS en Neon (cat_clientes,
-- historico_impresiones, historico_cambios_log): snake_case, BIGSERIAL + PK,
-- TIMESTAMPTZ creado_en/actualizado_en con DEFAULT now().

CREATE TABLE IF NOT EXISTS recibos_staging (
    id                          BIGSERIAL PRIMARY KEY,
    po_number                   VARCHAR(50)  NOT NULL,
    client_code                 VARCHAR(30)  NOT NULL REFERENCES cat_clientes(codigo),
    vendor_code                 VARCHAR(100),
    status                      VARCHAR(20)  NOT NULL DEFAULT 'BORRADOR'
                                    CHECK (status IN ('BORRADOR', 'EN_PROCESO', 'CONFIRMADO', 'IMPRESO', 'CANCELADO')),
    solicitante                 VARCHAR(100),
    total_piezas_esperadas      INTEGER      NOT NULL DEFAULT 0,
    total_piezas_paletizadas    INTEGER      NOT NULL DEFAULT 0,
    total_tarimas               INTEGER      NOT NULL DEFAULT 0,
    creado_en                   TIMESTAMPTZ  NOT NULL DEFAULT now(),
    actualizado_en              TIMESTAMPTZ  NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_recibos_staging_po_number ON recibos_staging (po_number);
CREATE INDEX IF NOT EXISTS idx_recibos_staging_status ON recibos_staging (status);
