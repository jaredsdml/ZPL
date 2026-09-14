-- Fase 1.3 (previa a servicios C#) - Persistencia del formato de calidad y maniobra
-- FOR-OPE-01/02: carátula de transporte/tiempos, checklist de calidad, y conteo ciego
-- de pallets. Cada una es 1:1 con recibos_staging (recibo_id UNIQUE + FK RESTRICT),
-- siguiendo el mismo patrón de cabecera/detalle usado en 001 y 002.

CREATE TABLE IF NOT EXISTS recibos_transporte_tiempos (
    id                          BIGSERIAL PRIMARY KEY,
    recibo_id                   BIGINT       NOT NULL UNIQUE REFERENCES recibos_staging(id) ON DELETE RESTRICT,
    tipo_operacion               VARCHAR(20)  NOT NULL DEFAULT 'DESCARGA'
                                    CHECK (tipo_operacion IN ('DESCARGA', 'CARGA')),
    linea_transporte             VARCHAR(100),
    nombre_operador              VARCHAR(150),
    placas_tracto                VARCHAR(30),
    placas_caja                  VARCHAR(30),
    contenedor                   VARCHAR(50),
    anden                        VARCHAR(20),
    sellos                       VARCHAR(100),
    sellos_colocados_por         VARCHAR(150),
    folio_cliente                VARCHAR(50),
    destino_entrega              VARCHAR(150),
    tipo_unidad                  VARCHAR(50),
    capacidad_unidad             VARCHAR(20)
                                    CHECK (capacidad_unidad IN ('1/4', '1/2', '3/4', 'COMPLETA')),
    maniobra_empresa             VARCHAR(100),
    hora_arribo                  TIMESTAMPTZ,
    hora_enrrampe                TIMESTAMPTZ,
    hora_inicio_proceso          TIMESTAMPTZ,
    hora_termino_proceso         TIMESTAMPTZ,
    hora_cierre_sistema          TIMESTAMPTZ,
    hora_entrega_doc_mdc         TIMESTAMPTZ,
    hora_entrega_doc_operador    TIMESTAMPTZ,
    observaciones                TEXT,
    creado_en                    TIMESTAMPTZ  NOT NULL DEFAULT now(),
    actualizado_en               TIMESTAMPTZ  NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS recibos_checklist_calidad (
    id                                      BIGSERIAL PRIMARY KEY,
    recibo_id                               BIGINT      NOT NULL UNIQUE REFERENCES recibos_staging(id) ON DELETE RESTRICT,

    -- Condiciones del vehículo
    vehiculo_limpieza_interior              VARCHAR(5)  DEFAULT 'SI' CHECK (vehiculo_limpieza_interior IN ('SI', 'NO', 'N/A')),
    vehiculo_sin_hoyos_goteras              VARCHAR(5)  DEFAULT 'SI' CHECK (vehiculo_sin_hoyos_goteras IN ('SI', 'NO', 'N/A')),
    vehiculo_sin_olores                     VARCHAR(5)  DEFAULT 'SI' CHECK (vehiculo_sin_olores IN ('SI', 'NO', 'N/A')),
    vehiculo_estructura_metalica_integra    VARCHAR(5)  DEFAULT 'SI' CHECK (vehiculo_estructura_metalica_integra IN ('SI', 'NO', 'N/A')),
    vehiculo_libre_plagas                   VARCHAR(5)  DEFAULT 'SI' CHECK (vehiculo_libre_plagas IN ('SI', 'NO', 'N/A')),
    vehiculo_libre_basura                   VARCHAR(5)  DEFAULT 'SI' CHECK (vehiculo_libre_basura IN ('SI', 'NO', 'N/A')),
    vehiculo_libre_grasa_quimicos           VARCHAR(5)  DEFAULT 'SI' CHECK (vehiculo_libre_grasa_quimicos IN ('SI', 'NO', 'N/A')),
    vehiculo_lamparas_operando              VARCHAR(5)  DEFAULT 'SI' CHECK (vehiculo_lamparas_operando IN ('SI', 'NO', 'N/A')),
    vehiculo_sujecion_funcional             VARCHAR(5)  DEFAULT 'SI' CHECK (vehiculo_sujecion_funcional IN ('SI', 'NO', 'N/A')),
    vehiculo_medidas_aptas                  VARCHAR(5)  DEFAULT 'SI' CHECK (vehiculo_medidas_aptas IN ('SI', 'NO', 'N/A')),
    vehiculo_sellos_colocados               VARCHAR(5)  DEFAULT 'SI' CHECK (vehiculo_sellos_colocados IN ('SI', 'NO', 'N/A')),
    vehiculo_certificado_fumigacion         VARCHAR(5)  DEFAULT 'SI' CHECK (vehiculo_certificado_fumigacion IN ('SI', 'NO', 'N/A')),
    vehiculo_apto_operacion                 BOOLEAN     DEFAULT true,

    -- Condiciones del producto
    producto_libre_plagas                   VARCHAR(5)  DEFAULT 'SI' CHECK (producto_libre_plagas IN ('SI', 'NO', 'N/A')),
    producto_libre_suciedad                 VARCHAR(5)  DEFAULT 'SI' CHECK (producto_libre_suciedad IN ('SI', 'NO', 'N/A')),
    producto_empaque_sin_dano               VARCHAR(5)  DEFAULT 'SI' CHECK (producto_empaque_sin_dano IN ('SI', 'NO', 'N/A')),
    producto_mercancia_sin_dano             VARCHAR(5)  DEFAULT 'SI' CHECK (producto_mercancia_sin_dano IN ('SI', 'NO', 'N/A')),
    producto_estibas_adecuadas              VARCHAR(5)  DEFAULT 'SI' CHECK (producto_estibas_adecuadas IN ('SI', 'NO', 'N/A')),
    producto_sin_pallets_ladeados           VARCHAR(5)  DEFAULT 'SI' CHECK (producto_sin_pallets_ladeados IN ('SI', 'NO', 'N/A')),
    producto_sobre_base                     VARCHAR(5)  DEFAULT 'SI' CHECK (producto_sobre_base IN ('SI', 'NO', 'N/A')),
    producto_tarimas_buenas_condiciones     VARCHAR(5)  DEFAULT 'SI' CHECK (producto_tarimas_buenas_condiciones IN ('SI', 'NO', 'N/A')),
    producto_playo_uniforme                 VARCHAR(5)  DEFAULT 'SI' CHECK (producto_playo_uniforme IN ('SI', 'NO', 'N/A')),
    producto_apto_operacion                 BOOLEAN     DEFAULT true,

    -- Actividades de reacondicionamiento
    reacond_traspaleo_pallets               INTEGER     DEFAULT 0,
    reacond_fumigacion_pallets              INTEGER     DEFAULT 0,
    reacond_cambio_playo_pallets            INTEGER     DEFAULT 0,
    reacond_cambio_tarima_pallets           INTEGER     DEFAULT 0,
    reacond_limpieza_pallets                INTEGER     DEFAULT 0,

    observaciones                           TEXT,
    verificado_por                          VARCHAR(100) NOT NULL,
    creado_en                               TIMESTAMPTZ  NOT NULL DEFAULT now(),
    actualizado_en                          TIMESTAMPTZ  NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS recibos_conteo_ciego_pallets (
    id                  BIGSERIAL PRIMARY KEY,
    recibo_id           BIGINT      NOT NULL UNIQUE REFERENCES recibos_staging(id) ON DELETE RESTRICT,
    tarima_estandar     INTEGER     NOT NULL DEFAULT 0,
    tarima_chep         INTEGER     NOT NULL DEFAULT 0,
    tarima_europalet    INTEGER     NOT NULL DEFAULT 0,
    tarima_tagon        INTEGER     NOT NULL DEFAULT 0,
    tarima_plastico     INTEGER     NOT NULL DEFAULT 0,
    granel              INTEGER     NOT NULL DEFAULT 0,
    total_pallets       INTEGER     GENERATED ALWAYS AS
                            (tarima_estandar + tarima_chep + tarima_europalet + tarima_tagon + tarima_plastico + granel) STORED,
    creado_en           TIMESTAMPTZ NOT NULL DEFAULT now(),
    actualizado_en      TIMESTAMPTZ NOT NULL DEFAULT now()
);
