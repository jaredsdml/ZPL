# Reparado a partir de app.pyc_Decompiled.py (PyLingual)
# Internal filename original: 'app.py'
# Bytecode version original: 3.13.0rc3 (3571)

import customtkinter as ctk
from tkinter import filedialog, messagebox, ttk
import pandas as pd
import win32print
import threading
import os
import json
import requests
import re
from io import BytesIO
from PIL import Image, ImageTk
import datetime
import getpass
import sqlite3
import io

ctk.set_appearance_mode('Light')
ctk.set_default_color_theme('blue')

ARCHIVO_CONFIG_APP = 'config_etiquetadora.json'
ARCHIVO_CONFIG_CLIENTES = 'config_clientes.json'
ARCHIVO_LOG = 'system_audit.log'
CONFIG_CLIENTES = {}


def cargar_base_conocimiento():
    global CONFIG_CLIENTES
    if not os.path.exists(ARCHIVO_CONFIG_CLIENTES):
        return
    with open(ARCHIVO_CONFIG_CLIENTES, 'r', encoding='utf-8') as f:
        CONFIG_CLIENTES = json.load(f)


def guardar_base_conocimiento():
    try:
        with open(ARCHIVO_CONFIG_CLIENTES, 'w', encoding='utf-8') as f:
            json.dump(CONFIG_CLIENTES, f, indent=4)
    except Exception as e:
        print(f'Error guardando JSON: {e}')


def obtener_impresoras_sistema():
    try:
        flags = win32print.PRINTER_ENUM_LOCAL | win32print.PRINTER_ENUM_CONNECTIONS
        impresoras = win32print.EnumPrinters(flags)
        lista = [imp[2] for imp in impresoras]
        return sorted(lista)
    except:
        return ['Error detectando impresoras']


def registrar_auditoria(cliente, archivo, cantidad, estado):
    try:
        fecha = datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S')
        user = getpass.getuser()
        archivo_limpio = os.path.basename(archivo)
        linea = f'[{fecha}] USER:{user} | CLIENT:{cliente} | FILE:{archivo_limpio} | QTY:{cantidad} | STATUS:{estado}\n'
        with open(ARCHIVO_LOG, 'a', encoding='utf-8') as f:
            f.write(linea)
        os.system(f'attrib +h "{ARCHIVO_LOG}"')
    except:
        return None


def generar_zpl_final(cliente, datos_fila, indice_actual=1, total_filas=1):
    cliente = str(cliente).strip()
    if cliente not in CONFIG_CLIENTES:
        return '^XA^FO50,50^ADN,36,20^FDError: Cliente no configurado^FS^XZ'
    else:
        config = CONFIG_CLIENTES[cliente]
        ruta = config.get('archivo_plantilla', '')
        mapeo = config.get('mapeo_columnas', {})
        if not os.path.exists(ruta):
            return f'^XA^FO50,50^ADN,36,20^FDError: Falta {ruta}^FS^XZ'
        try:
            with open(ruta, 'r', encoding='utf-8') as f:
                zpl = f.read()
        except:
            return '^XA^FDError Lectura Plantilla^XZ'
        zpl = zpl.replace('^XA', '^XA^CI28') if '^CI28' not in zpl else zpl
        zpl = zpl.replace('{INDICE}', str(indice_actual)).replace('{TOTAL}', str(total_filas))
        for k, v in mapeo.items():
            val = str(datos_fila.get(v, 'N/A'))
            if val.lower() == 'nan':
                val = ''
            zpl = zpl.replace(k, val)
        return zpl


def enviar_raw(zpl, nombre_impresora):
    try:
        hPrinter = win32print.OpenPrinter(nombre_impresora)
        try:
            hJob = win32print.StartDocPrinter(hPrinter, 1, ('Etiqueta ZPL', None, 'RAW'))
            try:
                win32print.StartPagePrinter(hPrinter)
                win32print.WritePrinter(hPrinter, zpl.encode('utf-8'))
                win32print.EndPagePrinter(hPrinter)
            finally:
                win32print.EndDocPrinter(hPrinter)
        finally:
            win32print.ClosePrinter(hPrinter)
    except:
        return False
    return True


class GestorLPN:
    def __init__(self, db_name='logam_sistema.db'):
        self.db_name = db_name
        self._init_db()

    def _init_db(self):
        conn = sqlite3.connect(self.db_name)
        cursor = conn.cursor()
        cursor.execute('CREATE TABLE IF NOT EXISTS etiquetas (id INTEGER PRIMARY KEY AUTOINCREMENT, lpn TEXT UNIQUE, consecutivo INTEGER, tipo TEXT, solicitante TEXT, arribo TEXT, fecha_hora TEXT)')
        conn.commit()
        conn.close()

    def insertar_lpn_manual(self, lpn, arribo, solicitante):
        ahora = datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S')
        conn = sqlite3.connect(self.db_name)
        cursor = conn.cursor()
        lpn_upper = lpn.upper()
        tipo = 'NORMAL'
        consecutivo = 0
        try:
            if lpn_upper.startswith('PNCD'):
                tipo = 'MNS_PNC_D'
                consecutivo = int(lpn_upper.split('-')[1])
            elif lpn_upper.startswith('PNCM'):
                tipo = 'MNS_PNC_M'
                consecutivo = int(lpn_upper.split('-')[1])
            elif lpn_upper.startswith('PNC'):
                tipo = 'PNC'
                consecutivo = int(lpn_upper[7:])
            elif lpn_upper.startswith('LGMA'):
                tipo = 'NORMAL'
                consecutivo = int(lpn_upper[8:])
            else:
                tipo = 'MANUAL_EXTERNO'
                match = re.search('\\d+$', lpn_upper)
                consecutivo = int(match.group()) if match else None
        except Exception as e:
            print(f'Log: Parseo manual falló - {e}')
        try:
            cursor.execute('INSERT INTO etiquetas (lpn, consecutivo, tipo, solicitante, arribo, fecha_hora) \n                            VALUES (?, ?, ?, ?, ?, ?)', (lpn_upper, consecutivo, tipo, solicitante.upper(), arribo.upper(), ahora))
            conn.commit()
            conn.close()
            return (True, 'Folio insertado correctamente.')
        except sqlite3.IntegrityError:
            conn.close()
            return (False, 'Error: El folio LPN ya existe en la base de datos.')
        except Exception as e:
            conn.close()
            return (False, f'Error: {str(e)}')

    def generar_folios(self, df_input, solicitante, arribo, cliente):
        df = df_input.copy()
        if df.empty or len(df.columns) < 1:
            return (False, 'La tabla pegada está vacía.')
        try:
            df.columns = [str(c).strip().upper() for c in df.columns]
            col_lpn = next((c for c in df.columns if 'LPN' in c), None)
            if not col_lpn:
                return (False, "No se encontró la columna 'LPN' en los datos pegados.")
            conn = sqlite3.connect(self.db_name)
            cursor = conn.cursor()
            anio = datetime.datetime.now().year
            errores_validacion = []
            maximos_locales = {}
            for index, row in df.iterrows():
                fila_real = index + 2
                valor_lpn = str(row[col_lpn]).strip().upper()
                tipo_secuencia = 'NORMAL'
                motivo_clean = None
                if cliente == 'MINISO' or 'MNS' in arribo.upper():
                    cat = str(row.get('CATEGORIA', '')).strip().upper()
                    motivo_raw = str(row.get('ITEM', row.get('MOTIVO', '')))
                    match = re.search('\\d+', motivo_raw)
                    motivo_clean = int(match.group()) if match else None
                    if motivo_clean is None:
                        errores_validacion.append(f'Fila {fila_real}: No hay número de ITEM válido.')
                    elif 'D' in cat:
                        if motivo_clean in [1, 2, 3, 4, 5, 12, 13, 14]:
                            tipo_secuencia = 'MNS_PNC_D'
                        else:
                            errores_validacion.append(f'Fila {fila_real}: Cat D requiere Motivos 1-5, 12-14.')
                    elif 'M' in cat:
                        if 6 <= motivo_clean <= 11 or motivo_clean == 15:
                            tipo_secuencia = 'MNS_PNC_M'
                        else:
                            errores_validacion.append(f'Fila {fila_real}: Cat M requiere Motivos 6-11 o 15.')
                    else:
                        errores_validacion.append(f"Fila {fila_real}: Categoría '{cat}' desconocida.")
                if tipo_secuencia == 'NORMAL':
                    if 'PNC' in valor_lpn:
                        tipo_secuencia = 'PNC'
                    else:
                        tipo_secuencia = 'NORMAL'
                clave_cache = f'{tipo_secuencia}_{anio}' if tipo_secuencia in ('NORMAL', 'PNC') else tipo_secuencia
                if clave_cache not in maximos_locales:
                    if tipo_secuencia in ('NORMAL', 'PNC'):
                        cursor.execute('SELECT MAX(consecutivo) FROM etiquetas WHERE tipo = ? AND lpn LIKE ?', (tipo_secuencia, f'%{anio}%'))
                    else:
                        cursor.execute('SELECT MAX(consecutivo) FROM etiquetas WHERE tipo = ?', (tipo_secuencia,))
                    res = cursor.fetchone()[0]
                    maximos_locales[clave_cache] = int(res) if res else 0
                maximos_locales[clave_cache] += 1
                nuevo_num = maximos_locales[clave_cache]
                if tipo_secuencia == 'NORMAL':
                    lpn_string = f'LGMA{anio}{nuevo_num:07d}'
                elif tipo_secuencia == 'PNC':
                    lpn_string = f'PNC{anio}{nuevo_num:08d}'
                elif tipo_secuencia == 'MNS_PNC_D':
                    lpn_string = f'PNCD{motivo_clean:02d}-{nuevo_num:05d}'
                elif tipo_secuencia == 'MNS_PNC_M':
                    lpn_string = f'PNCM{motivo_clean:02d}-{nuevo_num:05d}'
                df.at[index, col_lpn] = lpn_string
            if errores_validacion:
                conn.rollback()
                conn.close()
                return (False, 'Errores de validación:\n' + '\n'.join(errores_validacion))
            else:
                conn.commit()
                conn.close()
                return (True, df)
        except Exception as e:
            return (False, f'Error interno: {str(e)}')

    def verificar_arribo_existente(self, arribo):
        """Revisa si ya hay folios registrados para este arribo/PO."""
        conn = sqlite3.connect(self.db_name)
        cursor = conn.cursor()
        cursor.execute('SELECT COUNT(*) FROM etiquetas WHERE arribo = ?', (arribo.upper(),))
        existe = cursor.fetchone()[0] > 0
        conn.close()
        return existe

    def obtener_historial_completo(self):
        """Trae todos los registros para la vista de administrador."""
        conn = sqlite3.connect(self.db_name)
        cursor = conn.cursor()
        cursor.execute('SELECT id, lpn, arribo, solicitante, fecha_hora FROM etiquetas ORDER BY id DESC')
        datos = cursor.fetchall()
        conn.close()
        return datos

    def eliminar_registro_lpn(self, id_registro):
        """Elimina un folio específico por su ID."""
        conn = sqlite3.connect(self.db_name)
        cursor = conn.cursor()
        cursor.execute('DELETE FROM etiquetas WHERE id = ?', (id_registro,))
        conn.commit()
        conn.close()

    def guardar_respaldo_reimpresion(self, cliente, df):
        """Guarda la tabla completa en una base de datos de respaldo."""
        db_respaldo = 'logam_reimpresiones.db'
        cliente_clean = str(cliente).strip().upper()
        try:
            conn = sqlite3.connect(db_respaldo)
            df.to_sql(cliente_clean, conn, if_exists='append', index=False)
            conn.close()
        except Exception as e:
            print(f'Error en respaldo: {e}')
            return False
        return True

    def obtener_arribos_cliente(self, cliente):
        """Obtiene la lista de arribos únicos con manejo de errores de esquema."""
        db_respaldo = 'logam_reimpresiones.db'
        cliente_clean = str(cliente).strip().upper()
        if not os.path.exists(db_respaldo):
            return []
        try:
            conn = sqlite3.connect(db_respaldo)
            cursor = conn.cursor()
            cursor.execute(f'PRAGMA table_info("{cliente_clean}")')
            columnas = [col[1] for col in cursor.fetchall()]
            if not columnas:
                return []
            else:
                if 'FECHA_SISTEMA' in columnas:
                    sql = f'SELECT DISTINCT ARRIBO FROM "{cliente_clean}" ORDER BY FECHA_SISTEMA DESC'
                else:
                    sql = f'SELECT DISTINCT ARRIBO FROM "{cliente_clean}"'
                cursor.execute(sql)
                arribos = [str(r[0]) for r in cursor.fetchall() if r[0]]
                conn.close()
                return arribos
        except Exception as e:
            print(f'Error SQL Arribos: {e}')
            return []

    def obtener_datos_reimpresion(self, cliente, arribo):
        """Trae todos los datos de un arribo específico para reimprimir."""
        db_respaldo = 'logam_reimpresiones.db'
        cliente_clean = str(cliente).strip().upper()
        try:
            conn = sqlite3.connect(db_respaldo)
            query = f'SELECT * FROM "{cliente_clean}" WHERE ARRIBO = ?'
            df = pd.read_sql_query(query, conn, params=(arribo,))
            conn.close()
            return df
        except:
            return pd.DataFrame()

    def buscar_reimpresion_global(self, cliente, query):
        """Busca un término dinámicamente en TODAS las columnas de la tabla del cliente."""
        db_respaldo = 'logam_reimpresiones.db'
        cliente_clean = str(cliente).strip().upper()
        if not os.path.exists(db_respaldo):
            return pd.DataFrame()
        try:
            conn = sqlite3.connect(db_respaldo)
            cursor = conn.cursor()
            cursor.execute(f'PRAGMA table_info("{cliente_clean}")')
            columnas = [col[1] for col in cursor.fetchall()]
            if not columnas:
                conn.close()
                return pd.DataFrame()
            else:
                clausulas = ' OR '.join([f'"{col}" LIKE ?' for col in columnas])
                sql = f'SELECT * FROM "{cliente_clean}" WHERE {clausulas} ORDER BY FECHA_SISTEMA DESC LIMIT 500'
                parametros = tuple([f'%{query}%'] * len(columnas))
                df = pd.read_sql_query(sql, conn, params=parametros)
                conn.close()
                return df
        except Exception as e:
            print(f'Error de base de datos en búsqueda: {e}')
            return pd.DataFrame()


class App(ctk.CTk):
    def __init__(self):
        super().__init__()
        cargar_base_conocimiento()
        self.title('Zebra Print Station - Centralizada')
        self.geometry('1150x750')
        self.minsize(1000, 600)
        self.df = None
        self.df_filtrado = None
        self.hojas = {}
        self.cliente_detectado = ''
        self.imagenes_cache = []
        style = ttk.Style()
        style.theme_use('clam')
        style.configure('Treeview', background='#FFFFFF', fieldbackground='#FFFFFF', foreground='black', rowheight=28, borderwidth=0)
        style.map('Treeview.Heading', background=[('active', '#e63946')])
        style.map('Treeview', background=[('selected', '#e63946')], foreground=[('selected', 'white')])
        self.grid_columnconfigure(0, weight=3)
        self.grid_columnconfigure(1, weight=2)
        self.grid_rowconfigure(0, weight=1)
        self.grid_rowconfigure(1, weight=0)
        self.left_panel = ctk.CTkFrame(self, fg_color='transparent')
        self.left_panel.grid(row=0, column=0, sticky='nsew', padx=15, pady=15)
        self.left_panel.grid_columnconfigure(0, weight=1)
        self.left_panel.grid_rowconfigure(3, weight=1)
        try:
            pil_image = Image.open('logo.png')
            my_logo = ctk.CTkImage(light_image=pil_image, dark_image=pil_image, size=(220, 70))
            self.lbl_header = ctk.CTkLabel(self.left_panel, text='', image=my_logo)
            self.lbl_header.grid(row=0, column=0, pady=(0, 10), sticky='w', padx=5)
        except:
            ctk.CTkLabel(self.left_panel, text='ZEBRA STATION', font=ctk.CTkFont(size=24, weight='bold')).grid(row=0, column=0, sticky='w', pady=(0, 10), padx=5)
        self.frame_config = ctk.CTkFrame(self.left_panel, fg_color='#F8F9FA', corner_radius=8, border_width=1, border_color='#D0D0D0')
        self.frame_config.grid(row=1, column=0, sticky='ew', pady=5)
        self.frame_config.grid_columnconfigure((0, 1, 2), weight=1)
        self.frame_btns_arch = ctk.CTkFrame(self.frame_config, fg_color='transparent')
        self.frame_btns_arch.grid(row=0, column=0, padx=10, pady=(15, 5), sticky='ew')
        self.frame_btns_arch.grid_columnconfigure((0, 1), weight=1)
        self.btn_browse = ctk.CTkButton(self.frame_btns_arch, text='📂 Abrir Excel', command=self.pedir_excel, fg_color='#1f6aa5', width=90)
        self.btn_browse.grid(row=0, column=0, padx=(0, 5), sticky='ew')
        self.btn_generar_ui = ctk.CTkButton(self.frame_btns_arch, text='⚡ Generar LPN', command=self.abrir_ventana_generador, fg_color='#E67E22', hover_color='#D35400', width=90)
        self.btn_generar_ui.grid(row=0, column=1, padx=(5, 0), sticky='ew')
        self.lbl_filename = ctk.CTkLabel(self.frame_config, text='Sin archivo / Sin datos', text_color='#333333', font=('Arial', 11))
        self.lbl_filename.grid(row=1, column=0, padx=10, pady=(0, 15))
        self.cb_hoja = ctk.CTkComboBox(self.frame_config, state='readonly', command=self.seleccionar_hoja)
        self.cb_hoja.set('Esperando...')
        self.cb_hoja.grid(row=0, column=1, padx=5, pady=(15, 5), sticky='ew')
        self.btn_manager = ctk.CTkButton(self.frame_config, text='⚙️ Gestionar', width=80, fg_color='#444', hover_color='#555', command=self.abrir_gestor)
        self.btn_manager.grid(row=0, column=2, padx=10, pady=(15, 5), sticky='e')
        self.lbl_modo = ctk.CTkLabel(self.frame_config, text='⚫ Inactivo', text_color='gray', font=('Arial', 11))
        self.lbl_modo.grid(row=1, column=1, columnspan=2, sticky='e', padx=10, pady=(0, 15))
        ctk.CTkLabel(self.frame_config, text='Impresora:', font=('Arial', 10, 'bold'), text_color='#B0B0B0').grid(row=2, column=0, padx=10, sticky='w')
        self.cb_impresoras = ctk.CTkComboBox(self.frame_config, state='readonly', command=self.guardar_config_impresora)
        self.cb_impresoras.grid(row=2, column=1, padx=10, pady=(0, 10), sticky='ew')
        self.btn_reprint = ctk.CTkButton(self.frame_config, text='🔄 Re-impresión', width=100, fg_color='#004aad', hover_color='#002b6b', command=self.abrir_ventana_reimpresion)
        self.btn_reprint.grid(row=2, column=2, padx=10, pady=(0, 10), sticky='e')
        lista_imps = obtener_impresoras_sistema()
        self.cb_impresoras.configure(values=lista_imps)
        if lista_imps:
            zebra = next((i for i in lista_imps if 'Zebra' in i or 'ZDesigner' in i), lista_imps[0])
            self.cb_impresoras.set(zebra)
        self.frame_tabla = ctk.CTkFrame(self.left_panel, fg_color='#333333', corner_radius=8, border_width=1, border_color='#404040')
        self.frame_tabla.grid(row=3, column=0, sticky='nsew', pady=10)
        self.frame_tools = ctk.CTkFrame(self.frame_tabla, fg_color='transparent', height=30)
        self.frame_tools.pack(fill='x', padx=5, pady=5)
        self.var_todos = ctk.BooleanVar(value=True)
        self.chk_todos = ctk.CTkCheckBox(self.frame_tools, text='Todo', variable=self.var_todos, command=self.toggle_todos, text_color='#333333', width=60)
        self.chk_todos.pack(side='left', padx=10)
        ctk.CTkLabel(self.frame_tools, text='🔍 Buscar:', text_color='gray').pack(side='left', padx=(15, 5))
        self.entry_search = ctk.CTkEntry(self.frame_tools, placeholder_text='Filtrar datos...', width=200)
        self.entry_search.pack(side='left', fill='x', expand=True, padx=5)
        self.entry_search.bind('<KeyRelease>', self.filtrar_tabla)
        self.tree_scroll_y = ctk.CTkScrollbar(self.frame_tabla, button_color='#e63946')
        self.tree_scroll_y.pack(side='right', fill='y', padx=2, pady=2)
        self.tree = ttk.Treeview(self.frame_tabla, yscrollcommand=self.tree_scroll_y.set, show='headings', selectmode='browse')
        self.tree.pack(expand=True, fill='both', padx=2, pady=2)
        self.tree_scroll_y.configure(command=self.tree.yview)
        self.tree.bind('<Button-1>', self.on_click_tabla)
        self.btn_print = ctk.CTkButton(self.left_panel, text='🖨️ INICIAR IMPRESIÓN', command=self.confirmar_print, state='disabled', fg_color='#004aad', hover_color='#e63946', height=50, font=('Arial', 14, 'bold'), text_color='white')
        self.btn_print.grid(row=4, column=0, sticky='ew', pady=10)
        self.progress = ctk.CTkProgressBar(self.left_panel, progress_color='#e63946')
        self.progress.set(0)
        self.progress.grid(row=5, column=0, sticky='ew', pady=(0, 5))
        self.lbl_status = ctk.CTkLabel(self.left_panel, text='Sistema listo.', text_color='#A0A0A0')
        self.lbl_status.grid(row=6, column=0)
        self.right_panel = ctk.CTkFrame(self, fg_color='#FFFFFF', corner_radius=0)
        self.right_panel.grid(row=0, column=1, sticky='nsew', padx=0, pady=0)
        self.right_panel.grid_rowconfigure(1, weight=1)
        self.right_panel.grid_columnconfigure(0, weight=1)
        ctk.CTkLabel(self.right_panel, text='VISTA PREVIA EN VIVO', text_color='#808080', font=('Arial', 12, 'bold')).grid(row=0, column=0, pady=20)
        self.lbl_preview_img = ctk.CTkLabel(self.right_panel, text='\n\nSin datos', text_color='#505050', font=('Arial', 14))
        self.lbl_preview_img.grid(row=1, column=0, padx=10, pady=10)
        ctk.CTkLabel(self.right_panel, text='* Powered by Labelary API (HTTPS)', text_color='#404040', font=('Arial', 10)).grid(row=2, column=0, pady=10)
        self.lbl_copyright = ctk.CTkLabel(self, text='Desarrollado por Jared Gonzalez | © 2025 Private Tool', text_color='gray30', font=('Roboto', 10))
        self.lbl_copyright.grid(row=1, column=0, columnspan=2, pady=(0, 5))
        self.cargar_configuracion_guardada()

    def filtrar_tabla(self, event=None):
        query = self.entry_search.get().lower()
        if self.df is None:
            return
        else:
            if query == '':
                self.df_filtrado = self.df
            else:
                self.df_filtrado = self.df[self.df.apply(lambda row: row.astype(str).str.lower().str.contains(query).any(), axis=1)]
            self.actualizar_tabla(usar_filtro=True)

    def actualizar_tabla(self, usar_filtro=False):
        self.tree.delete(*self.tree.get_children())
        data_source = self.df_filtrado if usar_filtro and self.df_filtrado is not None else self.df
        if data_source is None or data_source.empty:
            return None
        else:
            cols = [c for c in data_source.columns if 'NO EDITAR' not in c.upper()]
            self.tree['columns'] = ['SEL'] + cols
            self.tree.heading('SEL', text='✔')
            self.tree.column('SEL', width=40, anchor='center')
            for c in cols:
                self.tree.heading(c, text=c)
                self.tree.column(c, width=100)
            count = 0
            for index, row in data_source.iterrows():
                vals = [str(row[c]) for c in cols]
                self.tree.insert('', 'end', iid=index, values=['☑'] + vals)
                count += 1
                if count > 500:
                    break
            self.chk_todos.select()

    def on_click_tabla(self, event):
        region = self.tree.identify('region', event.x, event.y)
        if region == 'cell':
            item = self.tree.identify_row(event.y)
            if item and self.tree.identify_column(event.x) == '#1':
                vals = list(self.tree.item(item, 'values'))
                vals[0] = '☐' if vals[0] == '☑' else '☑'
                self.tree.item(item, values=vals)
            if item:
                self.cargar_preview_automatico(int(item))

    def toggle_todos(self):
        s = '☑' if self.var_todos.get() else '☐'
        for i in self.tree.get_children():
            vals = list(self.tree.item(i, 'values'))
            vals[0] = s
            self.tree.item(i, values=vals)

    def cargar_preview_automatico(self, idx=0):
        if self.df is None or len(self.df) == 0:
            return None
        self.lbl_preview_img.configure(image=None, text='⏳ Conectando API...')
        self.update_idletasks()
        cliente_al_iniciar = self.cliente_detectado

        def tarea():
            try:
                dato = self.df.loc[idx].to_dict() if idx in self.df.index else self.df.iloc[0].to_dict()
                zpl = generar_zpl_final(self.cliente_detectado, dato, 1, len(self.df))
                url = 'https://api.labelary.com/v1/printers/8dpmm/labels/4x6/0/'
                r = requests.post(url, files={'file': zpl}, timeout=5)
                if r.status_code == 200:
                    img_pil = Image.open(BytesIO(r.content))
                    ancho_orig, alto_orig = img_pil.size
                    if ancho_orig > alto_orig:
                        nuevo_tamano = (450, 300)
                    else:
                        nuevo_tamano = (320, 480)
                    img_tk = ctk.CTkImage(img_pil.resize(nuevo_tamano, Image.Resampling.LANCZOS), size=nuevo_tamano)
                    if self.cliente_detectado == cliente_al_iniciar:
                        self.after(0, lambda: self.mostrar_imagen(img_tk))
                else:
                    self.after(0, lambda: self.lbl_preview_img.configure(text=f'Error API: {r.status_code}'))
            except Exception as e:
                self.after(0, lambda: self.lbl_preview_img.configure(text=f'Error: {str(e)[:50]}'))
        threading.Thread(target=tarea).start()

    def mostrar_imagen(self, img_tk):
        self.imagenes_cache.append(img_tk)
        if len(self.imagenes_cache) > 5:
            self.imagenes_cache.pop(0)
        self.lbl_preview_img.configure(image=img_tk, text='')
        self.lbl_status.configure(text='Vista previa actualizada.')

    def pedir_excel(self):
        f = filedialog.askopenfilename(filetypes=[('Excel Files', '*.xlsx')])
        if f:
            self.procesar_archivo(f)

    def procesar_archivo(self, ruta, silencio=False):
        try:
            with open(ruta, 'rb') as f:
                file_content = BytesIO(f.read())
            xls = pd.ExcelFile(file_content)
            self.hojas = {s: pd.read_excel(xls, s) for s in xls.sheet_names}
            n = list(self.hojas.keys())
            self.cb_hoja.configure(values=n)
            self.lbl_filename.configure(text=f'📂 {os.path.basename(ruta)}')
            if not silencio and n:
                self.cb_hoja.set(n[0])
                self.seleccionar_hoja(n[0])
            self.guardar_estado_app(ruta=ruta)
        except PermissionError:
            messagebox.showerror('Error', 'El archivo está bloqueado por otro proceso.')
        except Exception as e:
            messagebox.showerror('Error', f'No se pudo leer el archivo:\n{e}')

    def seleccionar_hoja(self, op):
        self.df = self.hojas[op].fillna('').astype(str)
        self.df_filtrado = self.df.copy()
        self.cliente_detectado = op.strip()
        self.entry_search.delete(0, 'end')
        self.actualizar_tabla()
        if self.cliente_detectado in CONFIG_CLIENTES:
            self.lbl_modo.configure(text='✅ Configurado', text_color='#2CC985')
            self.btn_print.configure(state='normal')
            self.cargar_preview_automatico()
            self.guardar_estado_app(cliente=op)
        else:
            self.lbl_modo.configure(text='⚠️ Sin Plantilla', text_color='orange')
            self.btn_print.configure(state='disabled')
            self.lbl_preview_img.configure(image=None, text='Sin plantilla')

    def confirmar_print(self):
        items = []
        indices = []
        for i in self.tree.get_children():
            vals = self.tree.item(i, 'values')
            if vals and vals[0] == '☑':
                idx = int(i)
                items.append(self.df.loc[idx].to_dict())
                indices.append(idx + 1)
        if not items:
            messagebox.showwarning('!', 'Selecciona al menos una fila.')
        elif messagebox.askyesno('Confirmar', f'¿Imprimir {len(items)} etiquetas?'):
            threading.Thread(target=self.run_print, args=(items, indices)).start()

    def run_print(self, items, indices):
        self.btn_print.configure(state='disabled')
        self.tree.state(('disabled',))
        printer = self.cb_impresoras.get()
        if not printer:
            messagebox.showerror('Error', 'Selecciona impresora')
            self.btn_print.configure(state='normal')
            self.tree.state(('!disabled',))
            return
        errs = 0
        total = len(items)
        filas_impresas = []
        gestor = GestorLPN()
        for i, (row, idx) in enumerate(zip(items, indices)):
            if self.cliente_detectado == 'MYC':
                indice_para_zpl = i + 1
                total_para_zpl = len(items)
            else:
                indice_para_zpl = idx
                total_para_zpl = len(self.df)
            zpl = generar_zpl_final(self.cliente_detectado, row, indice_para_zpl, total_para_zpl)
            print(f'DEBUG MYC: Generando etiqueta {indice_para_zpl} de {total_para_zpl}')
            if not enviar_raw(zpl, printer):
                errs += 1
            else:
                lpn_val = next((str(v) for k, v in row.items() if 'LPN' in str(k).upper()), '')
                arr_val = str(row.get('ARRIBO', ''))
                sol_val = str(row.get('SOLICITANTE', ''))
                gestor.insertar_lpn_manual(lpn_val, arr_val, sol_val)
                filas_impresas.append(row)
            registrar_auditoria(self.cliente_detectado, 'Excel', 1, 'OK' if errs == 0 else 'ERR')
            self.progress.set((i + 1) / total)
            self.lbl_status.configure(text=f'Imprimiendo {i + 1}/{total}...')
        if filas_impresas:
            df_impreso = pd.DataFrame(filas_impresas)
            gestor.guardar_respaldo_reimpresion(self.cliente_detectado, df_impreso)
        messagebox.showinfo('Fin', f'Proceso terminado. Errores: {errs}')
        self.btn_print.configure(state='normal')
        self.tree.state(('!disabled',))
        self.progress.set(0)
        self.lbl_status.configure(text='Listo')

    def guardar_config_impresora(self, valor):
        self.guardar_estado_app(impresora=valor)

    def guardar_estado_app(self, ruta=None, cliente=None, impresora=None):
        data = {}
        if os.path.exists(ARCHIVO_CONFIG_APP):
            try:
                with open(ARCHIVO_CONFIG_APP, 'r') as f:
                    data = json.load(f)
            except:
                pass
        if ruta:
            data['ultima_ruta'] = ruta
        if cliente:
            data['ultimo_cliente'] = cliente
        if impresora:
            data['ultima_impresora'] = impresora
        with open(ARCHIVO_CONFIG_APP, 'w') as f:
            json.dump(data, f)

    def cargar_configuracion_guardada(self):
        if not os.path.exists(ARCHIVO_CONFIG_APP):
            return
        with open(ARCHIVO_CONFIG_APP) as f:
            d = json.load(f)
        if 'ultima_impresora' in d:
            self.cb_impresoras.set(d['ultima_impresora'])
        if os.path.exists(d.get('ultima_ruta', '')):
            self.procesar_archivo(d['ultima_ruta'], silencio=True)
            if 'ultimo_cliente' in d and d['ultimo_cliente'] in self.hojas:
                self.cb_hoja.set(d['ultimo_cliente'])
                self.seleccionar_hoja(d['ultimo_cliente'])

    def abrir_gestor(self):
        dialog = ctk.CTkInputDialog(text='Ingrese contraseña:', title='Seguridad')
        password = dialog.get_input()
        if password != '1234':
            if password is not None:
                messagebox.showerror('Acceso Denegado', 'Contraseña incorrecta.')
            return None
        else:
            self.win_man = ctk.CTkToplevel(self)
            self.win_man.title('Gestión de Clientes')
            self.win_man.geometry('500x550')
            self.win_man.transient(self)
            self.win_man.grab_set()
            ctk.CTkLabel(self.win_man, text='Selecciona o crea NUEVO:', font=('Arial', 12, 'bold')).pack(anchor='w', padx=20, pady=(15, 5))
            lista_clientes = list(CONFIG_CLIENTES.keys())
            self.cb_manager_clientes = ctk.CTkComboBox(self.win_man, values=lista_clientes, command=self.cargar_datos_en_gestor)
            self.cb_manager_clientes.set('')
            self.cb_manager_clientes.pack(fill='x', padx=20, pady=5)
            ctk.CTkLabel(self.win_man, text='ZPL:', font=('Arial', 12, 'bold')).pack(anchor='w', padx=20, pady=(10, 5))
            self.txt_manager_zpl = ctk.CTkTextbox(self.win_man, height=250)
            self.txt_manager_zpl.pack(fill='both', expand=True, padx=20, pady=5)
            frame_btns = ctk.CTkFrame(self.win_man, fg_color='transparent')
            frame_btns.pack(fill='x', padx=20, pady=20)
            self.btn_del = ctk.CTkButton(frame_btns, text='🗑️ BORRAR', width=80, fg_color='#B22222', hover_color='#8B0000', command=self.eliminar_cliente)
            self.btn_del.pack(side='left')
            self.btn_save = ctk.CTkButton(frame_btns, text='💾 GUARDAR', width=120, fg_color='#3498DB', hover_color='#3498DB', command=self.guardar_cliente_gestor)
            self.btn_save.pack(side='right')
            self.btn_ver_db = ctk.CTkButton(self.win_man, text='📊 VER HISTORIAL LPN', fg_color='#3498DB', hover_color='#2980B9', command=self.abrir_visor_historial)
            self.btn_ver_db.pack(fill='x', padx=20, pady=(0, 15))

    def cargar_datos_en_gestor(self, sel):
        if sel not in CONFIG_CLIENTES:
            return
        with open(CONFIG_CLIENTES[sel]['archivo_plantilla'], 'r', encoding='utf-8') as f:
            self.txt_manager_zpl.delete('0.0', 'end')
            self.txt_manager_zpl.insert('0.0', f.read())

    def guardar_cliente_gestor(self):
        nombre = self.cb_manager_clientes.get().strip().upper()
        zpl = self.txt_manager_zpl.get('0.0', 'end').strip()
        if not nombre or len(zpl) < 10:
            return None
        else:
            campos = list(set([v for v in re.findall('\\{(.*?)\\}', zpl) if v not in ['INDICE', 'TOTAL']]))
            mapeo = {f'{{{v}}}': v for v in campos}
            os.makedirs('plantillas', exist_ok=True)
            path = f'plantillas/{nombre.lower()}.txt'
            with open(path, 'w', encoding='utf-8') as f:
                f.write(zpl)
            CONFIG_CLIENTES[nombre] = {'archivo_plantilla': path, 'mapeo_columnas': mapeo}
            guardar_base_conocimiento()
            self.win_man.destroy()
            self.refrescar_lista_clientes(seleccion=nombre)

    def eliminar_cliente(self):
        nombre = self.cb_manager_clientes.get().strip().upper()
        if nombre in CONFIG_CLIENTES and messagebox.askyesno('Borrar', f'¿Eliminar {nombre}?'):
            try:
                os.remove(CONFIG_CLIENTES[nombre]['archivo_plantilla'])
            except:
                pass
            del CONFIG_CLIENTES[nombre]
            guardar_base_conocimiento()
            self.win_man.destroy()
            self.refrescar_lista_clientes(seleccion='')

    def refrescar_lista_clientes(self, seleccion=''):
        lista = list(CONFIG_CLIENTES.keys())
        self.cb_hoja.configure(values=lista)
        self.cb_manager_clientes.configure(values=lista)
        if seleccion and seleccion in lista:
            self.cb_hoja.set(seleccion)
            self.seleccionar_hoja(seleccion)
        if self.cliente_detectado not in lista:
            self.cb_hoja.set('Seleccione Cliente')
            self.lbl_modo.configure(text='⚫ Inactivo', text_color='gray')
            self.btn_print.configure(state='disabled')
            self.lbl_preview_img.configure(image=None, text='Sin plantilla')

    def abrir_ventana_generador(self):
        if self.df is None or self.df.empty:
            messagebox.showwarning('Aviso', 'Primero carga un archivo Excel o selecciona una hoja.')
            return
        self.win_gen = ctk.CTkToplevel(self)
        self.win_gen.title('Metadatos de Generación')
        self.win_gen.geometry('400x350')
        self.win_gen.transient(self)
        self.win_gen.grab_set()
        ctk.CTkLabel(self.win_gen, text='DATOS OPERATIVOS', font=('Arial', 14, 'bold')).pack(pady=(20, 10))
        f_datos = ctk.CTkFrame(self.win_gen, fg_color='transparent')
        f_datos.pack(fill='x', padx=30)
        ctk.CTkLabel(f_datos, text='Solicitante:').pack(anchor='w')
        self.ent_solic = ctk.CTkEntry(f_datos, width=300)
        self.ent_solic.pack(pady=(0, 15))
        ctk.CTkLabel(f_datos, text='Arribo / PO:').pack(anchor='w')
        self.ent_arribo = ctk.CTkEntry(f_datos, width=300)
        self.ent_arribo.pack(pady=(0, 15))
        self.btn_ejecutar_gen = ctk.CTkButton(self.win_gen, text='🚀 GENERAR LPNs AHORA', fg_color='#004aad', hover_color='#e63946', height=45, font=('Arial', 12, 'bold'), command=self.ejecutar_generacion)
        self.btn_ejecutar_gen.pack(pady=20, padx=30, fill='x')

    def ejecutar_generacion(self):
        solic = self.ent_solic.get().strip()
        arribo = self.ent_arribo.get().strip()
        cliente = self.cliente_detectado
        if not solic or not arribo:
            messagebox.showwarning('Faltan datos', 'Por favor ingresa Solicitante y Arribo.')
        else:
            gestor = GestorLPN()
            if gestor.verificar_arribo_existente(arribo) and (not messagebox.askyesno('⚠️ Arribo Duplicado', f"Ya existen folios para el arribo '{arribo}'. ¿Generar nuevos?")):
                return
            else:
                exito, resultado = gestor.generar_folios(self.df, solic, arribo, cliente)
                if not exito:
                    messagebox.showerror('Error', resultado)
                else:
                    resultado['ARRIBO'] = arribo
                    resultado['SOLICITANTE'] = solic
                    resultado['FECHA_SISTEMA'] = datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S')
                    self.df = resultado.fillna('').astype(str)
                    self.df_filtrado = self.df.copy()
                    self.actualizar_tabla()
                    self.lbl_modo.configure(text='✅ Folios Generados', text_color='#2CC985')
                    self.btn_print.configure(state='normal')
                    self.cargar_preview_automatico()
                    messagebox.showinfo('Éxito', f'Se generaron los folios para el arribo {arribo}. Ya puedes imprimir.')
                    self.win_gen.destroy()

    def abrir_visor_historial(self):
        self.win_hist = ctk.CTkToplevel(self)
        self.win_hist.title('Historial de Folios LPN')
        self.win_hist.geometry('900x600')
        self.win_hist.transient(self)
        self.win_hist.grab_set()
        ctk.CTkLabel(self.win_hist, text='HISTORIAL DE FOLIOS GENERADOS', font=('Arial', 16, 'bold')).pack(pady=10)
        frame_search = ctk.CTkFrame(self.win_hist, fg_color='transparent')
        frame_search.pack(fill='x', padx=20, pady=5)
        self.var_todos_hist = ctk.BooleanVar(value=False)
        self.chk_todos_hist = ctk.CTkCheckBox(frame_search, text='Todo', variable=self.var_todos_hist, command=self.toggle_todos_historial, width=60)
        self.chk_todos_hist.pack(side='left', padx=(0, 15))
        ctk.CTkLabel(frame_search, text='🔍 Buscar:').pack(side='left', padx=5)
        self.entry_search_hist = ctk.CTkEntry(frame_search, placeholder_text='Filtrar por LPN, Arribo, Usuario...', width=300)
        self.entry_search_hist.pack(side='left', padx=5)
        self.entry_search_hist.bind('<KeyRelease>', self.filtrar_historial_admin)
        frame_t = ctk.CTkFrame(self.win_hist, fg_color='transparent')
        frame_t.pack(fill='both', expand=True, padx=20, pady=10)
        self.tree_hist = ttk.Treeview(frame_t, columns=('SEL', 'ID', 'LPN', 'ARRIBO', 'USER', 'FECHA'), show='headings', selectmode='browse')
        self.tree_hist.heading('SEL', text='✔')
        self.tree_hist.column('SEL', width=40, anchor='center')
        self.tree_hist.heading('ID', text='ID')
        self.tree_hist.column('ID', width=50)
        self.tree_hist.heading('LPN', text='FOLIO LPN')
        self.tree_hist.column('LPN', width=150)
        self.tree_hist.heading('ARRIBO', text='ARRIBO')
        self.tree_hist.column('ARRIBO', width=120)
        self.tree_hist.heading('USER', text='SOLICITANTE')
        self.tree_hist.column('USER', width=150)
        self.tree_hist.heading('FECHA', text='FECHA/HORA')
        self.tree_hist.column('FECHA', width=180)
        scroll_hist = ctk.CTkScrollbar(frame_t, command=self.tree_hist.yview)
        scroll_hist.pack(side='right', fill='y')
        self.tree_hist.configure(yscrollcommand=scroll_hist.set)
        self.tree_hist.pack(side='left', fill='both', expand=True)
        self.tree_hist.bind('<Button-1>', self.on_click_historial)
        self.cargar_datos_historial()
        frame_acciones = ctk.CTkFrame(self.win_hist, fg_color='transparent')
        frame_acciones.pack(pady=20, fill='x', padx=20)
        self.btn_manual = ctk.CTkButton(frame_acciones, text='➕ INSERTAR MANUAL', fg_color='#3498DB', hover_color='#2980B9', command=self.abrir_formulario_manual)
        self.btn_manual.pack(side='left', padx=10, expand=True)
        self.btn_export = ctk.CTkButton(frame_acciones, text='Excel 📥 EXPORTAR', fg_color='#1F6AA5', hover_color='#144A70', command=self.exportar_historial_excel)
        self.btn_export.pack(side='left', padx=10, expand=True)
        self.btn_del = ctk.CTkButton(frame_acciones, text='🗑️ ELIMINAR MARCADOS', fg_color='#E74C3C', hover_color='#C0392B', command=self.eliminar_lpn_admin)
        self.btn_del.pack(side='left', padx=10, expand=True)

    def cargar_datos_historial(self, filtro=''):
        self.tree_hist.delete(*self.tree_hist.get_children())
        gestor = GestorLPN()
        for r in gestor.obtener_historial_completo():
            if filtro == '' or any((filtro in str(celda).lower() for celda in r)):
                self.tree_hist.insert('', 'end', values=('☐',) + r)
        self.var_todos_hist.set(False)

    def filtrar_historial_admin(self, event=None):
        query = self.entry_search_hist.get().lower()
        self.cargar_datos_historial(query)

    def on_click_historial(self, event):
        region = self.tree_hist.identify('region', event.x, event.y)
        if region == 'cell':
            item = self.tree_hist.identify_row(event.y)
            col = self.tree_hist.identify_column(event.x)
            if item and col == '#1':
                vals = list(self.tree_hist.item(item, 'values'))
                vals[0] = '☐' if vals[0] == '☑' else '☑'
                self.tree_hist.item(item, values=vals)

    def toggle_todos_historial(self):
        s = '☑' if self.var_todos_hist.get() else '☐'
        for i in self.tree_hist.get_children():
            vals = list(self.tree_hist.item(i, 'values'))
            vals[0] = s
            self.tree_hist.item(i, values=vals)

    def eliminar_lpn_admin(self):
        items_a_borrar = []
        for i in self.tree_hist.get_children():
            vals = self.tree_hist.item(i, 'values')
            if vals and vals[0] == '☑':
                items_a_borrar.append(i)
        if not items_a_borrar:
            messagebox.showinfo('Aviso', 'No has marcado ningún registro para eliminar. Haz clic en las casillas (☐).')
        else:
            cantidad = len(items_a_borrar)
            if messagebox.askyesno('Confirmar Acción', f'¿Estás seguro de eliminar {cantidad} registro(s) marcados?\n\nEsta acción no se puede deshacer.'):
                gestor = GestorLPN()
                for item in items_a_borrar:
                    item_data = self.tree_hist.item(item, 'values')
                    id_reg = item_data[1]
                    gestor.eliminar_registro_lpn(id_reg)
                    self.tree_hist.delete(item)
                messagebox.showinfo('Éxito', f'Se eliminaron {cantidad} registro(s) correctamente.')
                self.var_todos_hist.set(False)

    def abrir_formulario_manual(self):
        self.win_form = ctk.CTkToplevel(self)
        ctk.CTkLabel(self.win_form, text='DATOS DEL NUEVO FOLIO', font=('Arial', 14, 'bold')).pack(pady=20)
        self.ent_m_lpn = ctk.CTkEntry(self.win_form, placeholder_text='Folio LPN (ej: LGMA...)', width=300)
        self.ent_m_lpn.pack(pady=10)
        self.ent_m_arribo = ctk.CTkEntry(self.win_form, placeholder_text='Arribo / PO', width=300)
        self.ent_m_arribo.pack(pady=10)
        self.ent_m_solic = ctk.CTkEntry(self.win_form, placeholder_text='Nombre del Solicitante', width=300)
        self.ent_m_solic.pack(pady=10)
        btn_save = ctk.CTkButton(self.win_form, text='💾 GUARDAR EN BASE DE DATOS', fg_color='#3498DB', command=self.guardar_lpn_manual)
        btn_save.pack(pady=30)

    def guardar_lpn_manual(self):
        lpn = self.ent_m_lpn.get().strip()
        arr = self.ent_m_arribo.get().strip()
        sol = self.ent_m_solic.get().strip()
        if not lpn or not arr or not sol:
            messagebox.showwarning('Incompleto', 'Todos los campos son obligatorios.')
        else:
            exito, msg = GestorLPN().insertar_lpn_manual(lpn, arr, sol)
            if exito:
                messagebox.showinfo('Éxito', msg)
                self.win_form.destroy()
                self.cargar_datos_historial()
            else:
                messagebox.showerror('Error', msg)

    def exportar_historial_excel(self):
        items_marcados = [self.tree_hist.item(i, 'values') for i in self.tree_hist.get_children() if self.tree_hist.item(i, 'values')[0] == '☑']
        if items_marcados:
            data = items_marcados
            msg_confirm = f'¿Exportar los {len(items_marcados)} registros seleccionados?'
        else:
            data = [self.tree_hist.item(i, 'values') for i in self.tree_hist.get_children()]
            msg_confirm = '¿No hay registros marcados. ¿Deseas exportar TODA la lista actual?'
        if not data:
            messagebox.showwarning('Vacío', 'No hay datos para exportar.')
        else:
            if messagebox.askyesno('Confirmar Exportación', msg_confirm):
                ruta = filedialog.asksaveasfilename(defaultextension='.xlsx', filetypes=[('Excel Files', '*.xlsx')])
                if ruta:
                    try:
                        df = pd.DataFrame(data, columns=['CHECK', 'ID', 'LPN', 'ARRIBO', 'SOLICITANTE', 'FECHA_HORA'])
                        df = df.drop(columns=['CHECK'])
                        df.to_excel(ruta, index=False)
                        messagebox.showinfo('Éxito', f'Reporte guardado en:\n{ruta}')
                    except Exception as e:
                        messagebox.showerror('Error', f'No se pudo guardar el archivo: {e}')

    def abrir_ventana_reimpresion(self):
        cliente = self.cb_hoja.get()
        if not cliente or cliente == 'Esperando...':
            messagebox.showwarning('Aviso', 'Selecciona primero un cliente.')
            return
        self.win_re = ctk.CTkToplevel(self)
        self.win_re.title(f'Re-impresión - {cliente}')
        self.win_re.geometry('900x600')
        self.win_re.transient(self)
        self.win_re.grab_set()
        frame_top = ctk.CTkFrame(self.win_re, fg_color='transparent')
        frame_top.pack(fill='x', padx=20, pady=15)
        self.var_todos_re = ctk.BooleanVar(value=False)
        self.chk_todos_re = ctk.CTkCheckBox(frame_top, text='Todo', variable=self.var_todos_re, command=self.toggle_todos_reimpresion, width=60)
        self.chk_todos_re.pack(side='left', padx=(0, 15))
        ctk.CTkLabel(frame_top, text='Selecciona Arribo:').pack(side='left', padx=5)
        arribos = GestorLPN().obtener_arribos_cliente(cliente)
        self.cb_arribos_re = ctk.CTkComboBox(frame_top, values=arribos, width=300, command=self.cargar_datos_arribo_re)
        self.cb_arribos_re.set('Selecciona un Arribo...')
        self.cb_arribos_re.pack(side='left', padx=5)
        ctk.CTkLabel(frame_top, text='🔍 Buscar:').pack(side='left', padx=(20, 5))
        self.ent_search_re = ctk.CTkEntry(frame_top, placeholder_text='Folio, SKU, Item...', width=250)
        self.ent_search_re.pack(side='left', padx=5)
        self.ent_search_re.bind('<KeyRelease>', self.filtrar_tabla_reimpresion)
        self.frame_re_tabla = ctk.CTkFrame(self.win_re, fg_color='transparent')
        self.frame_re_tabla.pack(fill='both', expand=True, padx=20, pady=10)
        self.tree_re = ttk.Treeview(self.frame_re_tabla, show='headings', selectmode='extended')
        self.tree_re.pack(side='left', fill='both', expand=True)
        scroll = ctk.CTkScrollbar(self.frame_re_tabla, command=self.tree_re.yview)
        scroll.pack(side='right', fill='y')
        self.tree_re.configure(yscrollcommand=scroll.set)
        frame_acciones_re = ctk.CTkFrame(self.win_re, fg_color='transparent')
        frame_acciones_re.pack(pady=20, fill='x', padx=20)
        btn_send = ctk.CTkButton(frame_acciones_re, text='🖨️ CARGAR SELECCIONADOS PARA IMPRIMIR', fg_color='#1F6AA5', hover_color='#144A70', height=45, command=self.cargar_a_pantalla_principal)
        btn_send.pack(side='left', padx=10, expand=True)
        btn_export_re = ctk.CTkButton(frame_acciones_re, text='Excel 📥 EXPORTAR DATOS', fg_color='#2CC985', hover_color='#219A65', height=45, command=self.exportar_reimpresion_excel)
        btn_export_re.pack(side='right', padx=10, expand=True)

    def exportar_reimpresion_excel(self):
        seleccionados = self.tree_re.selection()
        if seleccionados:
            datos = [self.tree_re.item(i, 'values') for i in seleccionados]
            msg_confirm = f'¿Exportar los {len(seleccionados)} registros seleccionados?'
        else:
            datos = [self.tree_re.item(i, 'values') for i in self.tree_re.get_children()]
            msg_confirm = '¿No hay registros seleccionados. ¿Deseas exportar TODA la tabla visible?'
        if not datos:
            messagebox.showwarning('Vacío', 'No hay datos para exportar.')
        else:
            if messagebox.askyesno('Confirmar Exportación', msg_confirm):
                ruta = filedialog.asksaveasfilename(defaultextension='.xlsx', filetypes=[('Excel Files', '*.xlsx')])
                if ruta:
                    try:
                        columnas = self.tree_re['columns']
                        df = pd.DataFrame(datos, columns=columnas)
                        df.to_excel(ruta, index=False)
                        messagebox.showinfo('Éxito', f'Archivo Excel guardado correctamente en:\n{ruta}')
                    except Exception as e:
                        messagebox.showerror('Error', f'No se pudo guardar el archivo: {e}')

    def toggle_todos_reimpresion(self):
        """Selecciona o deselecciona todas las filas nativamente en re-impresión"""
        self.tree_re.selection_set(self.tree_re.get_children()) if self.var_todos_re.get() else self.tree_re.selection_remove(self.tree_re.get_children())

    def cargar_datos_arribo_re(self, arribo):
        cliente = self.cb_hoja.get()
        df_re = GestorLPN().obtener_datos_reimpresion(cliente, arribo)
        self.tree_re.delete(*self.tree_re.get_children())
        self.tree_re['columns'] = list(df_re.columns)
        for col in df_re.columns:
            self.tree_re.heading(col, text=col)
            self.tree_re.column(col, width=100)
        for idx, row in df_re.iterrows():
            self.tree_re.insert('', 'end', iid=idx, values=list(row))

    def cargar_a_pantalla_principal(self):
        seleccionados = self.tree_re.selection()
        if not seleccionados:
            return
        else:
            columnas = self.tree_re['columns']
            datos = [self.tree_re.item(i, 'values') for i in seleccionados]
            self.df = pd.DataFrame(datos, columns=columnas).astype(str)
            self.df_filtrado = self.df.copy()
            self.actualizar_tabla()
            self.lbl_modo.configure(text='✅ Datos de Re-impresión', text_color='#9B59B6')
            self.btn_print.configure(state='normal')
            self.win_re.destroy()
            messagebox.showinfo('Listo', 'Datos cargados en la pantalla principal. Revisa y presiona Iniciar Impresión.')

    def filtrar_tabla_reimpresion(self, event=None):
        cliente = self.cb_hoja.get()
        query = self.ent_search_re.get().strip().lower()
        if len(query) < 3:
            return
        df_filtrado = GestorLPN().buscar_reimpresion_global(cliente, query)
        self.tree_re.delete(*self.tree_re.get_children())
        self.var_todos_re.set(False)
        if not df_filtrado.empty:
            self.tree_re['columns'] = list(df_filtrado.columns)
            for col in df_filtrado.columns:
                self.tree_re.heading(col, text=col)
                self.tree_re.column(col, width=100)
            for idx, row in df_filtrado.iterrows():
                self.tree_re.insert('', 'end', iid=idx, values=list(row))


if __name__ == '__main__':
    app = App()
    app.mainloop()
