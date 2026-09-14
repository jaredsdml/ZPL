-- Fase 1.2 - Módulo de paletización y recibo digital
-- Detalle de pallets virtuales por recibo, y bitácora de auditoría de ediciones
-- (mismo patrón que historico_cambios_log para historico_impresiones).

CREATE TABLE IF NOT EXISTS recibos_staging_detalle (
    id                      BIGSERIAL PRIMARY KEY,
    recibo_id               BIGINT       NOT NULL REFERENCES recibos_staging(id) ON DELETE RESTRICT,
    item_number             VARCHAR(50)  NOT NULL,
    descripcion             VARCHAR(255),
    lote                    VARCHAR(50),
    atributo                VARCHAR(50),
    cantidad_piezas         INTEGER      NOT NULL DEFAULT 0,
    cantidad_cajas          INTEGER      DEFAULT 0,
    es_pnc                  BOOLEAN      NOT NULL DEFAULT false,
    hu_id                   VARCHAR(50),
    tipo_tarima              VARCHAR(30),
    consecutivo_tarima      INTEGER      NOT NULL,
    activo                  BOOLEAN      NOT NULL DEFAULT true,
    creado_por               VARCHAR(100) NOT NULL,
    modificado_por           VARCHAR(100),
    creado_en                TIMESTAMPTZ  NOT NULL DEFAULT now(),
    actualizado_en            TIMESTAMPTZ  NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_recibos_staging_detalle_recibo_id ON recibos_staging_detalle (recibo_id);
CREATE INDEX IF NOT EXISTS idx_recibos_staging_detalle_item_number ON recibos_staging_detalle (item_number);
CREATE INDEX IF NOT EXISTS idx_recibos_staging_detalle_hu_id ON recibos_staging_detalle (hu_id);
CREATE INDEX IF NOT EXISTS idx_recibos_staging_detalle_activo ON recibos_staging_detalle (activo);

CREATE TABLE IF NOT EXISTS recibos_staging_cambios_log (
    id                      BIGSERIAL PRIMARY KEY,
    recibo_id               BIGINT       NOT NULL REFERENCES recibos_staging(id),
    detalle_id              BIGINT       REFERENCES recibos_staging_detalle(id),
    entidad_modificada      VARCHAR(50)  NOT NULL,
    campo_modificado        VARCHAR(50)  NOT NULL,
    valor_anterior          TEXT,
    valor_nuevo             TEXT,
    motivo_cambio           VARCHAR(255),
    usuario                 VARCHAR(100) NOT NULL,
    fecha_cambio             TIMESTAMPTZ  NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_recibos_staging_cambios_log_recibo_id ON recibos_staging_cambios_log (recibo_id);
CREATE INDEX IF NOT EXISTS idx_recibos_staging_cambios_log_detalle_id ON recibos_staging_cambios_log (detalle_id);
