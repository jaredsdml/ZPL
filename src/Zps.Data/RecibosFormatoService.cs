using Npgsql;
using Zps.Data.Models.Recibos;

namespace Zps.Data;

/// <summary>
/// Persistencia del formato de calidad y maniobra FOR-OPE-01/02: carátula de
/// transporte/tiempos, checklist de inspección de calidad y conteo ciego de pallets. Las
/// tres tablas son 1:1 con un recibo (recibo_id UNIQUE + FK RESTRICT a recibos_staging), así
/// que guardar siempre es un upsert por recibo_id (ON CONFLICT (recibo_id) DO UPDATE).
/// </summary>
public sealed class RecibosFormatoService
{
    private readonly NpgsqlDataSource _dataSource;

    public RecibosFormatoService(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    // ---- Transporte / tiempos ----

    public async Task<ReciboTransporteTiemposModel> GuardarTransporteTiemposAsync(
        ReciboTransporteTiemposModel modelo, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO recibos_transporte_tiempos (
                recibo_id, tipo_operacion, linea_transporte, nombre_operador, placas_tracto, placas_caja,
                contenedor, anden, sellos, sellos_colocados_por, folio_cliente, destino_entrega, tipo_unidad,
                capacidad_unidad, maniobra_empresa, hora_arribo, hora_enrrampe, hora_inicio_proceso,
                hora_termino_proceso, hora_cierre_sistema, hora_entrega_doc_mdc, hora_entrega_doc_operador,
                observaciones, actualizado_en)
            VALUES (
                @recibo_id, @tipo_operacion, @linea_transporte, @nombre_operador, @placas_tracto, @placas_caja,
                @contenedor, @anden, @sellos, @sellos_colocados_por, @folio_cliente, @destino_entrega, @tipo_unidad,
                @capacidad_unidad, @maniobra_empresa, @hora_arribo, @hora_enrrampe, @hora_inicio_proceso,
                @hora_termino_proceso, @hora_cierre_sistema, @hora_entrega_doc_mdc, @hora_entrega_doc_operador,
                @observaciones, now())
            ON CONFLICT (recibo_id) DO UPDATE SET
                tipo_operacion = excluded.tipo_operacion,
                linea_transporte = excluded.linea_transporte,
                nombre_operador = excluded.nombre_operador,
                placas_tracto = excluded.placas_tracto,
                placas_caja = excluded.placas_caja,
                contenedor = excluded.contenedor,
                anden = excluded.anden,
                sellos = excluded.sellos,
                sellos_colocados_por = excluded.sellos_colocados_por,
                folio_cliente = excluded.folio_cliente,
                destino_entrega = excluded.destino_entrega,
                tipo_unidad = excluded.tipo_unidad,
                capacidad_unidad = excluded.capacidad_unidad,
                maniobra_empresa = excluded.maniobra_empresa,
                hora_arribo = excluded.hora_arribo,
                hora_enrrampe = excluded.hora_enrrampe,
                hora_inicio_proceso = excluded.hora_inicio_proceso,
                hora_termino_proceso = excluded.hora_termino_proceso,
                hora_cierre_sistema = excluded.hora_cierre_sistema,
                hora_entrega_doc_mdc = excluded.hora_entrega_doc_mdc,
                hora_entrega_doc_operador = excluded.hora_entrega_doc_operador,
                observaciones = excluded.observaciones,
                actualizado_en = now()
            RETURNING id, recibo_id, tipo_operacion, linea_transporte, nombre_operador, placas_tracto, placas_caja,
                contenedor, anden, sellos, sellos_colocados_por, folio_cliente, destino_entrega, tipo_unidad,
                capacidad_unidad, maniobra_empresa, hora_arribo, hora_enrrampe, hora_inicio_proceso,
                hora_termino_proceso, hora_cierre_sistema, hora_entrega_doc_mdc, hora_entrega_doc_operador,
                observaciones, creado_en, actualizado_en;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("recibo_id", modelo.ReciboId);
        command.Parameters.AddWithValue("tipo_operacion", modelo.TipoOperacion);
        command.Parameters.AddWithValue("linea_transporte", (object?)modelo.LineaTransporte ?? DBNull.Value);
        command.Parameters.AddWithValue("nombre_operador", (object?)modelo.NombreOperador ?? DBNull.Value);
        command.Parameters.AddWithValue("placas_tracto", (object?)modelo.PlacasTracto ?? DBNull.Value);
        command.Parameters.AddWithValue("placas_caja", (object?)modelo.PlacasCaja ?? DBNull.Value);
        command.Parameters.AddWithValue("contenedor", (object?)modelo.Contenedor ?? DBNull.Value);
        command.Parameters.AddWithValue("anden", (object?)modelo.Anden ?? DBNull.Value);
        command.Parameters.AddWithValue("sellos", (object?)modelo.Sellos ?? DBNull.Value);
        command.Parameters.AddWithValue("sellos_colocados_por", (object?)modelo.SellosColocadosPor ?? DBNull.Value);
        command.Parameters.AddWithValue("folio_cliente", (object?)modelo.FolioCliente ?? DBNull.Value);
        command.Parameters.AddWithValue("destino_entrega", (object?)modelo.DestinoEntrega ?? DBNull.Value);
        command.Parameters.AddWithValue("tipo_unidad", (object?)modelo.TipoUnidad ?? DBNull.Value);
        command.Parameters.AddWithValue("capacidad_unidad", (object?)modelo.CapacidadUnidad ?? DBNull.Value);
        command.Parameters.AddWithValue("maniobra_empresa", (object?)modelo.ManiobraEmpresa ?? DBNull.Value);
        command.Parameters.AddWithValue("hora_arribo", (object?)modelo.HoraArribo ?? DBNull.Value);
        command.Parameters.AddWithValue("hora_enrrampe", (object?)modelo.HoraEnrrampe ?? DBNull.Value);
        command.Parameters.AddWithValue("hora_inicio_proceso", (object?)modelo.HoraInicioProceso ?? DBNull.Value);
        command.Parameters.AddWithValue("hora_termino_proceso", (object?)modelo.HoraTerminoProceso ?? DBNull.Value);
        command.Parameters.AddWithValue("hora_cierre_sistema", (object?)modelo.HoraCierreSistema ?? DBNull.Value);
        command.Parameters.AddWithValue("hora_entrega_doc_mdc", (object?)modelo.HoraEntregaDocMdc ?? DBNull.Value);
        command.Parameters.AddWithValue("hora_entrega_doc_operador", (object?)modelo.HoraEntregaDocOperador ?? DBNull.Value);
        command.Parameters.AddWithValue("observaciones", (object?)modelo.Observaciones ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return LeerTransporteTiempos(reader);
    }

    public async Task<ReciboTransporteTiemposModel?> ObtenerTransporteTiemposAsync(
        long reciboId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, recibo_id, tipo_operacion, linea_transporte, nombre_operador, placas_tracto, placas_caja,
                contenedor, anden, sellos, sellos_colocados_por, folio_cliente, destino_entrega, tipo_unidad,
                capacidad_unidad, maniobra_empresa, hora_arribo, hora_enrrampe, hora_inicio_proceso,
                hora_termino_proceso, hora_cierre_sistema, hora_entrega_doc_mdc, hora_entrega_doc_operador,
                observaciones, creado_en, actualizado_en
            FROM recibos_transporte_tiempos
            WHERE recibo_id = @recibo_id;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("recibo_id", reciboId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? LeerTransporteTiempos(reader) : null;
    }

    // ---- Checklist de calidad ----

    public async Task<ReciboChecklistCalidadModel> GuardarChecklistCalidadAsync(
        ReciboChecklistCalidadModel modelo, CancellationToken cancellationToken = default)
    {
        ValidarChecklist(modelo);

        const string sql = """
            INSERT INTO recibos_checklist_calidad (
                recibo_id,
                vehiculo_limpieza_interior, vehiculo_sin_hoyos_goteras, vehiculo_sin_olores,
                vehiculo_estructura_metalica_integra, vehiculo_libre_plagas, vehiculo_libre_basura,
                vehiculo_libre_grasa_quimicos, vehiculo_lamparas_operando, vehiculo_sujecion_funcional,
                vehiculo_medidas_aptas, vehiculo_sellos_colocados, vehiculo_certificado_fumigacion,
                vehiculo_apto_operacion,
                producto_libre_plagas, producto_libre_suciedad, producto_empaque_sin_dano,
                producto_mercancia_sin_dano, producto_estibas_adecuadas, producto_sin_pallets_ladeados,
                producto_sobre_base, producto_tarimas_buenas_condiciones, producto_playo_uniforme,
                producto_apto_operacion,
                reacond_traspaleo_pallets, reacond_fumigacion_pallets, reacond_cambio_playo_pallets,
                reacond_cambio_tarima_pallets, reacond_limpieza_pallets,
                observaciones, verificado_por, actualizado_en)
            VALUES (
                @recibo_id,
                @vehiculo_limpieza_interior, @vehiculo_sin_hoyos_goteras, @vehiculo_sin_olores,
                @vehiculo_estructura_metalica_integra, @vehiculo_libre_plagas, @vehiculo_libre_basura,
                @vehiculo_libre_grasa_quimicos, @vehiculo_lamparas_operando, @vehiculo_sujecion_funcional,
                @vehiculo_medidas_aptas, @vehiculo_sellos_colocados, @vehiculo_certificado_fumigacion,
                @vehiculo_apto_operacion,
                @producto_libre_plagas, @producto_libre_suciedad, @producto_empaque_sin_dano,
                @producto_mercancia_sin_dano, @producto_estibas_adecuadas, @producto_sin_pallets_ladeados,
                @producto_sobre_base, @producto_tarimas_buenas_condiciones, @producto_playo_uniforme,
                @producto_apto_operacion,
                @reacond_traspaleo_pallets, @reacond_fumigacion_pallets, @reacond_cambio_playo_pallets,
                @reacond_cambio_tarima_pallets, @reacond_limpieza_pallets,
                @observaciones, @verificado_por, now())
            ON CONFLICT (recibo_id) DO UPDATE SET
                vehiculo_limpieza_interior = excluded.vehiculo_limpieza_interior,
                vehiculo_sin_hoyos_goteras = excluded.vehiculo_sin_hoyos_goteras,
                vehiculo_sin_olores = excluded.vehiculo_sin_olores,
                vehiculo_estructura_metalica_integra = excluded.vehiculo_estructura_metalica_integra,
                vehiculo_libre_plagas = excluded.vehiculo_libre_plagas,
                vehiculo_libre_basura = excluded.vehiculo_libre_basura,
                vehiculo_libre_grasa_quimicos = excluded.vehiculo_libre_grasa_quimicos,
                vehiculo_lamparas_operando = excluded.vehiculo_lamparas_operando,
                vehiculo_sujecion_funcional = excluded.vehiculo_sujecion_funcional,
                vehiculo_medidas_aptas = excluded.vehiculo_medidas_aptas,
                vehiculo_sellos_colocados = excluded.vehiculo_sellos_colocados,
                vehiculo_certificado_fumigacion = excluded.vehiculo_certificado_fumigacion,
                vehiculo_apto_operacion = excluded.vehiculo_apto_operacion,
                producto_libre_plagas = excluded.producto_libre_plagas,
                producto_libre_suciedad = excluded.producto_libre_suciedad,
                producto_empaque_sin_dano = excluded.producto_empaque_sin_dano,
                producto_mercancia_sin_dano = excluded.producto_mercancia_sin_dano,
                producto_estibas_adecuadas = excluded.producto_estibas_adecuadas,
                producto_sin_pallets_ladeados = excluded.producto_sin_pallets_ladeados,
                producto_sobre_base = excluded.producto_sobre_base,
                producto_tarimas_buenas_condiciones = excluded.producto_tarimas_buenas_condiciones,
                producto_playo_uniforme = excluded.producto_playo_uniforme,
                producto_apto_operacion = excluded.producto_apto_operacion,
                reacond_traspaleo_pallets = excluded.reacond_traspaleo_pallets,
                reacond_fumigacion_pallets = excluded.reacond_fumigacion_pallets,
                reacond_cambio_playo_pallets = excluded.reacond_cambio_playo_pallets,
                reacond_cambio_tarima_pallets = excluded.reacond_cambio_tarima_pallets,
                reacond_limpieza_pallets = excluded.reacond_limpieza_pallets,
                observaciones = excluded.observaciones,
                verificado_por = excluded.verificado_por,
                actualizado_en = now()
            RETURNING id, recibo_id,
                vehiculo_limpieza_interior, vehiculo_sin_hoyos_goteras, vehiculo_sin_olores,
                vehiculo_estructura_metalica_integra, vehiculo_libre_plagas, vehiculo_libre_basura,
                vehiculo_libre_grasa_quimicos, vehiculo_lamparas_operando, vehiculo_sujecion_funcional,
                vehiculo_medidas_aptas, vehiculo_sellos_colocados, vehiculo_certificado_fumigacion,
                vehiculo_apto_operacion,
                producto_libre_plagas, producto_libre_suciedad, producto_empaque_sin_dano,
                producto_mercancia_sin_dano, producto_estibas_adecuadas, producto_sin_pallets_ladeados,
                producto_sobre_base, producto_tarimas_buenas_condiciones, producto_playo_uniforme,
                producto_apto_operacion,
                reacond_traspaleo_pallets, reacond_fumigacion_pallets, reacond_cambio_playo_pallets,
                reacond_cambio_tarima_pallets, reacond_limpieza_pallets,
                observaciones, verificado_por, creado_en, actualizado_en;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("recibo_id", modelo.ReciboId);
        command.Parameters.AddWithValue("vehiculo_limpieza_interior", (object?)modelo.VehiculoLimpiezaInterior ?? DBNull.Value);
        command.Parameters.AddWithValue("vehiculo_sin_hoyos_goteras", (object?)modelo.VehiculoSinHoyosGoteras ?? DBNull.Value);
        command.Parameters.AddWithValue("vehiculo_sin_olores", (object?)modelo.VehiculoSinOlores ?? DBNull.Value);
        command.Parameters.AddWithValue("vehiculo_estructura_metalica_integra", (object?)modelo.VehiculoEstructuraMetalicaIntegra ?? DBNull.Value);
        command.Parameters.AddWithValue("vehiculo_libre_plagas", (object?)modelo.VehiculoLibrePlagas ?? DBNull.Value);
        command.Parameters.AddWithValue("vehiculo_libre_basura", (object?)modelo.VehiculoLibreBasura ?? DBNull.Value);
        command.Parameters.AddWithValue("vehiculo_libre_grasa_quimicos", (object?)modelo.VehiculoLibreGrasaQuimicos ?? DBNull.Value);
        command.Parameters.AddWithValue("vehiculo_lamparas_operando", (object?)modelo.VehiculoLamparasOperando ?? DBNull.Value);
        command.Parameters.AddWithValue("vehiculo_sujecion_funcional", (object?)modelo.VehiculoSujecionFuncional ?? DBNull.Value);
        command.Parameters.AddWithValue("vehiculo_medidas_aptas", (object?)modelo.VehiculoMedidasAptas ?? DBNull.Value);
        command.Parameters.AddWithValue("vehiculo_sellos_colocados", (object?)modelo.VehiculoSellosColocados ?? DBNull.Value);
        command.Parameters.AddWithValue("vehiculo_certificado_fumigacion", (object?)modelo.VehiculoCertificadoFumigacion ?? DBNull.Value);
        command.Parameters.AddWithValue("vehiculo_apto_operacion", (object?)modelo.VehiculoAptoOperacion ?? DBNull.Value);
        command.Parameters.AddWithValue("producto_libre_plagas", (object?)modelo.ProductoLibrePlagas ?? DBNull.Value);
        command.Parameters.AddWithValue("producto_libre_suciedad", (object?)modelo.ProductoLibreSuciedad ?? DBNull.Value);
        command.Parameters.AddWithValue("producto_empaque_sin_dano", (object?)modelo.ProductoEmpaqueSinDano ?? DBNull.Value);
        command.Parameters.AddWithValue("producto_mercancia_sin_dano", (object?)modelo.ProductoMercanciaSinDano ?? DBNull.Value);
        command.Parameters.AddWithValue("producto_estibas_adecuadas", (object?)modelo.ProductoEstibasAdecuadas ?? DBNull.Value);
        command.Parameters.AddWithValue("producto_sin_pallets_ladeados", (object?)modelo.ProductoSinPalletsLadeados ?? DBNull.Value);
        command.Parameters.AddWithValue("producto_sobre_base", (object?)modelo.ProductoSobreBase ?? DBNull.Value);
        command.Parameters.AddWithValue("producto_tarimas_buenas_condiciones", (object?)modelo.ProductoTarimasBuenasCondiciones ?? DBNull.Value);
        command.Parameters.AddWithValue("producto_playo_uniforme", (object?)modelo.ProductoPlayoUniforme ?? DBNull.Value);
        command.Parameters.AddWithValue("producto_apto_operacion", (object?)modelo.ProductoAptoOperacion ?? DBNull.Value);
        command.Parameters.AddWithValue("reacond_traspaleo_pallets", (object?)modelo.ReacondTraspaleoPallets ?? DBNull.Value);
        command.Parameters.AddWithValue("reacond_fumigacion_pallets", (object?)modelo.ReacondFumigacionPallets ?? DBNull.Value);
        command.Parameters.AddWithValue("reacond_cambio_playo_pallets", (object?)modelo.ReacondCambioPlayoPallets ?? DBNull.Value);
        command.Parameters.AddWithValue("reacond_cambio_tarima_pallets", (object?)modelo.ReacondCambioTarimaPallets ?? DBNull.Value);
        command.Parameters.AddWithValue("reacond_limpieza_pallets", (object?)modelo.ReacondLimpiezaPallets ?? DBNull.Value);
        command.Parameters.AddWithValue("observaciones", (object?)modelo.Observaciones ?? DBNull.Value);
        command.Parameters.AddWithValue("verificado_por", modelo.VerificadoPor);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return LeerChecklistCalidad(reader);
    }

    public async Task<ReciboChecklistCalidadModel?> ObtenerChecklistCalidadAsync(
        long reciboId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, recibo_id,
                vehiculo_limpieza_interior, vehiculo_sin_hoyos_goteras, vehiculo_sin_olores,
                vehiculo_estructura_metalica_integra, vehiculo_libre_plagas, vehiculo_libre_basura,
                vehiculo_libre_grasa_quimicos, vehiculo_lamparas_operando, vehiculo_sujecion_funcional,
                vehiculo_medidas_aptas, vehiculo_sellos_colocados, vehiculo_certificado_fumigacion,
                vehiculo_apto_operacion,
                producto_libre_plagas, producto_libre_suciedad, producto_empaque_sin_dano,
                producto_mercancia_sin_dano, producto_estibas_adecuadas, producto_sin_pallets_ladeados,
                producto_sobre_base, producto_tarimas_buenas_condiciones, producto_playo_uniforme,
                producto_apto_operacion,
                reacond_traspaleo_pallets, reacond_fumigacion_pallets, reacond_cambio_playo_pallets,
                reacond_cambio_tarima_pallets, reacond_limpieza_pallets,
                observaciones, verificado_por, creado_en, actualizado_en
            FROM recibos_checklist_calidad
            WHERE recibo_id = @recibo_id;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("recibo_id", reciboId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? LeerChecklistCalidad(reader) : null;
    }

    // ---- Conteo ciego de pallets ----

    public async Task<ReciboConteoCiegoModel> GuardarConteoCiegoAsync(
        ReciboConteoCiegoModel modelo, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO recibos_conteo_ciego_pallets (
                recibo_id, tarima_estandar, tarima_chep, tarima_europalet, tarima_tagon, tarima_plastico, granel, actualizado_en)
            VALUES (@recibo_id, @tarima_estandar, @tarima_chep, @tarima_europalet, @tarima_tagon, @tarima_plastico, @granel, now())
            ON CONFLICT (recibo_id) DO UPDATE SET
                tarima_estandar = excluded.tarima_estandar,
                tarima_chep = excluded.tarima_chep,
                tarima_europalet = excluded.tarima_europalet,
                tarima_tagon = excluded.tarima_tagon,
                tarima_plastico = excluded.tarima_plastico,
                granel = excluded.granel,
                actualizado_en = now()
            RETURNING id, recibo_id, tarima_estandar, tarima_chep, tarima_europalet, tarima_tagon, tarima_plastico, granel, total_pallets, creado_en, actualizado_en;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("recibo_id", modelo.ReciboId);
        command.Parameters.AddWithValue("tarima_estandar", modelo.TarimaEstandar);
        command.Parameters.AddWithValue("tarima_chep", modelo.TarimaChep);
        command.Parameters.AddWithValue("tarima_europalet", modelo.TarimaEuropalet);
        command.Parameters.AddWithValue("tarima_tagon", modelo.TarimaTagon);
        command.Parameters.AddWithValue("tarima_plastico", modelo.TarimaPlastico);
        command.Parameters.AddWithValue("granel", modelo.Granel);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return LeerConteoCiego(reader);
    }

    public async Task<ReciboConteoCiegoModel?> ObtenerConteoCiegoAsync(
        long reciboId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, recibo_id, tarima_estandar, tarima_chep, tarima_europalet, tarima_tagon, tarima_plastico, granel, total_pallets, creado_en, actualizado_en
            FROM recibos_conteo_ciego_pallets
            WHERE recibo_id = @recibo_id;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("recibo_id", reciboId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? LeerConteoCiego(reader) : null;
    }

    /// <summary>
    /// Valida client-side los 21 campos SI/NO/N-A antes del round-trip a Neon, para poder
    /// devolver de una vez la lista completa de campos inválidos en vez de que el operador
    /// tenga que corregir uno por uno tras cada violación del CHECK de la base de datos.
    /// </summary>
    private static void ValidarChecklist(ReciboChecklistCalidadModel modelo)
    {
        var invalidos = new List<string>();
        void Verificar(string nombre, string? valor)
        {
            if (!ReciboChecklistCalidadModel.EsValorValido(valor))
            {
                invalidos.Add(nombre);
            }
        }

        Verificar(nameof(modelo.VehiculoLimpiezaInterior), modelo.VehiculoLimpiezaInterior);
        Verificar(nameof(modelo.VehiculoSinHoyosGoteras), modelo.VehiculoSinHoyosGoteras);
        Verificar(nameof(modelo.VehiculoSinOlores), modelo.VehiculoSinOlores);
        Verificar(nameof(modelo.VehiculoEstructuraMetalicaIntegra), modelo.VehiculoEstructuraMetalicaIntegra);
        Verificar(nameof(modelo.VehiculoLibrePlagas), modelo.VehiculoLibrePlagas);
        Verificar(nameof(modelo.VehiculoLibreBasura), modelo.VehiculoLibreBasura);
        Verificar(nameof(modelo.VehiculoLibreGrasaQuimicos), modelo.VehiculoLibreGrasaQuimicos);
        Verificar(nameof(modelo.VehiculoLamparasOperando), modelo.VehiculoLamparasOperando);
        Verificar(nameof(modelo.VehiculoSujecionFuncional), modelo.VehiculoSujecionFuncional);
        Verificar(nameof(modelo.VehiculoMedidasAptas), modelo.VehiculoMedidasAptas);
        Verificar(nameof(modelo.VehiculoSellosColocados), modelo.VehiculoSellosColocados);
        Verificar(nameof(modelo.VehiculoCertificadoFumigacion), modelo.VehiculoCertificadoFumigacion);
        Verificar(nameof(modelo.ProductoLibrePlagas), modelo.ProductoLibrePlagas);
        Verificar(nameof(modelo.ProductoLibreSuciedad), modelo.ProductoLibreSuciedad);
        Verificar(nameof(modelo.ProductoEmpaqueSinDano), modelo.ProductoEmpaqueSinDano);
        Verificar(nameof(modelo.ProductoMercanciaSinDano), modelo.ProductoMercanciaSinDano);
        Verificar(nameof(modelo.ProductoEstibasAdecuadas), modelo.ProductoEstibasAdecuadas);
        Verificar(nameof(modelo.ProductoSinPalletsLadeados), modelo.ProductoSinPalletsLadeados);
        Verificar(nameof(modelo.ProductoSobreBase), modelo.ProductoSobreBase);
        Verificar(nameof(modelo.ProductoTarimasBuenasCondiciones), modelo.ProductoTarimasBuenasCondiciones);
        Verificar(nameof(modelo.ProductoPlayoUniforme), modelo.ProductoPlayoUniforme);

        if (invalidos.Count > 0)
        {
            throw new ArgumentException(
                $"Valor de checklist inválido (debe ser SI/NO/N-A o null) en: {string.Join(", ", invalidos)}.",
                nameof(modelo));
        }
    }

    private static ReciboTransporteTiemposModel LeerTransporteTiempos(NpgsqlDataReader reader) => new(
        Id: reader.GetInt64(reader.GetOrdinal("id")),
        ReciboId: reader.GetInt64(reader.GetOrdinal("recibo_id")),
        TipoOperacion: reader.GetString(reader.GetOrdinal("tipo_operacion")),
        LineaTransporte: reader.GetStringOrNull("linea_transporte"),
        NombreOperador: reader.GetStringOrNull("nombre_operador"),
        PlacasTracto: reader.GetStringOrNull("placas_tracto"),
        PlacasCaja: reader.GetStringOrNull("placas_caja"),
        Contenedor: reader.GetStringOrNull("contenedor"),
        Anden: reader.GetStringOrNull("anden"),
        Sellos: reader.GetStringOrNull("sellos"),
        SellosColocadosPor: reader.GetStringOrNull("sellos_colocados_por"),
        FolioCliente: reader.GetStringOrNull("folio_cliente"),
        DestinoEntrega: reader.GetStringOrNull("destino_entrega"),
        TipoUnidad: reader.GetStringOrNull("tipo_unidad"),
        CapacidadUnidad: reader.GetStringOrNull("capacidad_unidad"),
        ManiobraEmpresa: reader.GetStringOrNull("maniobra_empresa"),
        HoraArribo: reader.GetDateTimeOffsetOrNull("hora_arribo"),
        HoraEnrrampe: reader.GetDateTimeOffsetOrNull("hora_enrrampe"),
        HoraInicioProceso: reader.GetDateTimeOffsetOrNull("hora_inicio_proceso"),
        HoraTerminoProceso: reader.GetDateTimeOffsetOrNull("hora_termino_proceso"),
        HoraCierreSistema: reader.GetDateTimeOffsetOrNull("hora_cierre_sistema"),
        HoraEntregaDocMdc: reader.GetDateTimeOffsetOrNull("hora_entrega_doc_mdc"),
        HoraEntregaDocOperador: reader.GetDateTimeOffsetOrNull("hora_entrega_doc_operador"),
        Observaciones: reader.GetStringOrNull("observaciones"),
        CreadoEn: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("creado_en")),
        ActualizadoEn: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("actualizado_en")));

    private static ReciboChecklistCalidadModel LeerChecklistCalidad(NpgsqlDataReader reader) => new(
        Id: reader.GetInt64(reader.GetOrdinal("id")),
        ReciboId: reader.GetInt64(reader.GetOrdinal("recibo_id")),
        VehiculoLimpiezaInterior: reader.GetStringOrNull("vehiculo_limpieza_interior"),
        VehiculoSinHoyosGoteras: reader.GetStringOrNull("vehiculo_sin_hoyos_goteras"),
        VehiculoSinOlores: reader.GetStringOrNull("vehiculo_sin_olores"),
        VehiculoEstructuraMetalicaIntegra: reader.GetStringOrNull("vehiculo_estructura_metalica_integra"),
        VehiculoLibrePlagas: reader.GetStringOrNull("vehiculo_libre_plagas"),
        VehiculoLibreBasura: reader.GetStringOrNull("vehiculo_libre_basura"),
        VehiculoLibreGrasaQuimicos: reader.GetStringOrNull("vehiculo_libre_grasa_quimicos"),
        VehiculoLamparasOperando: reader.GetStringOrNull("vehiculo_lamparas_operando"),
        VehiculoSujecionFuncional: reader.GetStringOrNull("vehiculo_sujecion_funcional"),
        VehiculoMedidasAptas: reader.GetStringOrNull("vehiculo_medidas_aptas"),
        VehiculoSellosColocados: reader.GetStringOrNull("vehiculo_sellos_colocados"),
        VehiculoCertificadoFumigacion: reader.GetStringOrNull("vehiculo_certificado_fumigacion"),
        VehiculoAptoOperacion: reader.GetBooleanOrNull("vehiculo_apto_operacion"),
        ProductoLibrePlagas: reader.GetStringOrNull("producto_libre_plagas"),
        ProductoLibreSuciedad: reader.GetStringOrNull("producto_libre_suciedad"),
        ProductoEmpaqueSinDano: reader.GetStringOrNull("producto_empaque_sin_dano"),
        ProductoMercanciaSinDano: reader.GetStringOrNull("producto_mercancia_sin_dano"),
        ProductoEstibasAdecuadas: reader.GetStringOrNull("producto_estibas_adecuadas"),
        ProductoSinPalletsLadeados: reader.GetStringOrNull("producto_sin_pallets_ladeados"),
        ProductoSobreBase: reader.GetStringOrNull("producto_sobre_base"),
        ProductoTarimasBuenasCondiciones: reader.GetStringOrNull("producto_tarimas_buenas_condiciones"),
        ProductoPlayoUniforme: reader.GetStringOrNull("producto_playo_uniforme"),
        ProductoAptoOperacion: reader.GetBooleanOrNull("producto_apto_operacion"),
        ReacondTraspaleoPallets: reader.GetInt32OrNull("reacond_traspaleo_pallets"),
        ReacondFumigacionPallets: reader.GetInt32OrNull("reacond_fumigacion_pallets"),
        ReacondCambioPlayoPallets: reader.GetInt32OrNull("reacond_cambio_playo_pallets"),
        ReacondCambioTarimaPallets: reader.GetInt32OrNull("reacond_cambio_tarima_pallets"),
        ReacondLimpiezaPallets: reader.GetInt32OrNull("reacond_limpieza_pallets"),
        Observaciones: reader.GetStringOrNull("observaciones"),
        VerificadoPor: reader.GetString(reader.GetOrdinal("verificado_por")),
        CreadoEn: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("creado_en")),
        ActualizadoEn: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("actualizado_en")));

    private static ReciboConteoCiegoModel LeerConteoCiego(NpgsqlDataReader reader) => new(
        Id: reader.GetInt64(reader.GetOrdinal("id")),
        ReciboId: reader.GetInt64(reader.GetOrdinal("recibo_id")),
        TarimaEstandar: reader.GetInt32(reader.GetOrdinal("tarima_estandar")),
        TarimaChep: reader.GetInt32(reader.GetOrdinal("tarima_chep")),
        TarimaEuropalet: reader.GetInt32(reader.GetOrdinal("tarima_europalet")),
        TarimaTagon: reader.GetInt32(reader.GetOrdinal("tarima_tagon")),
        TarimaPlastico: reader.GetInt32(reader.GetOrdinal("tarima_plastico")),
        Granel: reader.GetInt32(reader.GetOrdinal("granel")),
        TotalPallets: reader.GetInt32(reader.GetOrdinal("total_pallets")),
        CreadoEn: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("creado_en")),
        ActualizadoEn: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("actualizado_en")));
}
