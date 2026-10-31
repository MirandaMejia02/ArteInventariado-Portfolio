# ArteInventariado

Sistema de escritorio para gestión de inventario, punto de venta y administración comercial de un negocio minorista de artesanías. Integra ventas, existencias, caja, compras, crédito, pedidos, reportes y recuperación de datos en una aplicación local para Windows.

**v1.16.4 · C# / .NET 10 · WinForms · EF Core / SQLite · Windows x64**

La V1 utiliza SQLite en el mismo equipo que ejecuta la aplicación. La operación comercial local no depende de conexión a Internet; el respaldo externo en la nube es opcional. Excel funciona como mecanismo de importación y exportación, mientras que la base de datos mantiene el estado del sistema.

## Estado del proyecto

- **Implementado:** catálogo con SKU, inventario por ubicación, POS tradicional y rápido, correcciones de ventas, caja, clientes y crédito, proveedores y compras, cotizaciones, pedidos personalizados, pagos a empleados, analítica, reportes, auditoría y configuración.
- **Implementado como infraestructura:** respaldo y restauración, respaldo externo opcional, recuperación de datos, estaciones de consulta, instalación para todos los usuarios de Windows y actualización con recuperación.
- **Preparado, con integración física pendiente:** adaptadores específicos para impresoras de tickets, etiquetadoras y gaveta, sujetos a disponer del modelo real y validar su funcionamiento.
- **Pendiente de mejora:** comunicación y traspaso seguro cuando la aplicación permanece abierta en otra sesión de Windows; consolidación de documentación, código y publicación por versión.
- **Planificado para V2:** API y base centralizada para operación conectada y múltiples dispositivos. Estos componentes no forman parte de la V1.

## Tecnologías

- **C# y .NET 10:** aplicación y servicios, tipos anulables y operaciones asíncronas.
- **Windows Forms:** interfaz de escritorio, paneles por rol, formularios operativos y navegación.
- **Entity Framework Core 10 y SQLite:** persistencia relacional, consultas LINQ, configuraciones de entidades y migraciones Code First.
- **Microsoft.Extensions.DependencyInjection:** composición de servicios y gestión de dependencias.
- **ClosedXML:** plantillas, importación y exportación de archivos Excel `.xlsx`.
- **BCrypt.Net-Next:** verificación y almacenamiento de contraseñas mediante hash.
- **LiveCharts2:** gráficos y visualización de indicadores en WinForms.
- **SkiaSharp y ZXing.Net:** generación y representación de códigos y etiquetas.
- **AWS SDK para S3 y STS:** respaldo externo y recuperación autorizada, cuando se configura esta capacidad.
- **xUnit, Microsoft.NET.Test.Sdk y Coverlet:** pruebas automatizadas y herramientas de cobertura.
- **PowerShell:** compilación, ejecución aislada de desarrollo, publicación y preparación de paquetes.

Las versiones concretas de las dependencias se encuentran en los archivos `.csproj`.

## Funcionalidades implementadas

### Acceso, usuarios y permisos

- Autenticación local, validación de cuentas activas y cambio de contraseña.
- Roles **ADMIN** y **EMPLEADO**, con paneles y accesos diferenciados.
- Administrador principal protegido, sin introducir un tercer rol privilegiado.
- Gestión de empleados y delegación de permisos por operación: ventas, descuentos, métodos de pago, caja, correcciones, compras, crédito, imágenes, etiquetas y respaldos, entre otros.
- Restricciones adicionales para administrar otras cuentas con rol ADMIN.
- Reautenticación administrativa en operaciones críticas que la requieren, como ajustes de conteo de inventario.
- Registro de accesos y operaciones relevantes en la bitácora.

Una instalación nueva solicita configurar la contraseña del administrador principal. No se documentan credenciales predeterminadas ni cuentas de demostración con contraseñas fijas.

### Productos, categorías y SKU

- Catálogo con códigos internos, descripción, precios, costos, estados y mínimos de inventario.
- Categorías y subcategorías, con filtros y protección de referencias históricas al eliminar registros.
- Modos de producto `SIMPLE`, `SURTIDO` y `POR_VARIANTES`.
- Identidad operativa mediante SKU; un producto por variantes puede tener varios SKU.
- Códigos textuales estables que conservan ceros iniciales.
- Búsqueda y filtros por categoría, subcategoría, estado, modo, imágenes, ubicación, existencias y códigos de barras, según la pantalla.
- Consulta de inventario para empleados con información limitada por sus permisos.

### Imágenes, códigos y etiquetas

- Imágenes asociadas a productos y SKU, administradas como archivos fuera de SQLite.
- Galerías, imagen principal, vistas previas y miniaturas regenerables.
- Resolución visual: imagen del SKU, imagen del producto y, finalmente, imagen de sustitución.
- Importación de imágenes en lote con coincidencia por código de producto o SKU y revisión manual de asignaciones ambiguas.
- Comparación de imagen actual y nueva, con conservación o sustitución explícita.
- Varios códigos por SKU, selección de principal, activación y desactivación, y unicidad global.
- Búsqueda exacta de códigos, generación interna, Code 128 y QR para identificación del catálogo.
- Preparación y vista previa de etiquetas por perfil de tamaño y por lote.
- Entrada mediante lector USB HID compatible con teclado, además de captura manual.

Los códigos QR del catálogo identifican artículos; no constituyen una integración de cobro electrónico.

### Inventario por SKU y ubicación

La fuente canónica de existencias es `inventario_saldos`, identificada por **SKU + ubicación**. El sistema diferencia existencia física, cantidad reservada y disponibilidad:

```text
Disponible = Existencia física − Reservado
```

- Ubicaciones operativas: `EXHIBICION`, `BODEGA` y `SIN_ASIGNAR`.
- Transferencias explícitas entre ubicaciones sin alterar el total físico.
- Recepción de compras por SKU y ubicación; Bodega es el destino habitual por defecto.
- Venta normal desde Exhibición, con transferencia explícita cuando se requiere abastecerla desde Bodega.
- Ajuste de conteo físico por SKU y ubicación, con motivo, autorización y control de concurrencia.
- Registro de mermas por cantidad, ubicación, motivo, usuario y fecha.
- Reservas para pedidos que reducen la disponibilidad sin disminuir la existencia física.
- Mínimos de Exhibición, alertas de reposición y revisión de inventario inmovilizado.
- Historial de movimientos, consultas de saldos y conciliación con las proyecciones de compatibilidad.

El inventario histórico sin ubicación conocida se conserva como `SIN_ASIGNAR`. `Producto.StockActual` se mantiene por compatibilidad y proyección; las nuevas operaciones utilizan el motor canónico.

### Punto de venta

- POS tradicional y POS rápido, ambos conectados al mismo servicio de ventas y a las mismas reglas de caja, pagos, permisos e inventario.
- Catálogo visual, búsqueda, lectura de códigos, carrito y selección exacta de SKU.
- Interacción con teclado, controles táctiles y atajos en el POS rápido.
- Consulta rápida de productos y variantes sin perder el carrito.
- Descuentos por línea y generales, sujetos a permisos y validaciones.
- Registro de pagos en efectivo, transferencia y Bitcoin, incluidos pagos combinados.
- Cálculo de importes pagados y cambio, con separación de los efectos de cada método.
- Integración con clientes, ventas a crédito y documentos relacionados.
- Protección del carrito al salir, historial de ventas, detalle de comprobantes y consulta de ventas propias del día.
- Ticket, vista previa, exportación PDF y reimpresión.

Los métodos de transferencia y Bitcoin se registran administrativamente. No se afirma integración con una pasarela bancaria, terminal de tarjetas, billetera o red de criptomonedas.

### Devoluciones, cambios y anulaciones

- Operaciones de corrección enlazadas a la venta original y auditadas.
- Devoluciones por producto y cantidad, cambios y reembolsos según el flujo correspondiente.
- Tratamiento distinto para mercancía devuelta vendible y dañada; una devolución dañada no incrementa stock vendible.
- Conservación de los documentos de origen y del historial de correcciones.
- En v1.16.4, anulación administrativa de un registro erróneo cuando se confirma que no existieron venta, pago ni entrega reales.
- En ese caso específico, devolución de cantidades a Exhibición y exclusión de los pagos originales de los cálculos de caja y reportes afectados, sin registrar un reembolso ficticio.
- Bloqueos ante combinaciones no admitidas, como determinadas correcciones previas, pagos de pedidos o abonos a crédito.

La regularización del efectivo y la corrección de una venta son operaciones diferentes. Ajustar un conteo de caja no convierte una venta errónea en una venta válida ni corrige por sí solo sus reportes.

### Clientes y cuentas por cobrar

- Directorio, fichas e historial comercial de clientes.
- Normalización de datos y advertencias de posibles duplicados.
- Políticas generales y particulares de crédito, límites, plazos y condiciones configurables.
- Ventas a crédito, cuentas por cobrar, detalle de deuda, abonos y comprobantes de pago.
- Seguimiento de saldos, vencimientos y alertas de crédito.
- Permisos específicos para consultar información, conceder crédito y registrar abonos.

### Proveedores, artesanos, compras y cuentas por pagar

- Directorio y perfiles de proveedores o artesanos, con asociaciones a productos.
- Compras, detalles, estados y seguimiento de recepciones.
- Recepciones parciales o totales por SKU y ubicación.
- Pagos iniciales, compras a crédito, cuentas por pagar y abonos a proveedores.
- Historial y comprobantes de pagos.
- Registro del origen de fondos: caja física, fondos externos del propietario o fondos del negocio fuera de caja.
- Validaciones de caja abierta y disponibilidad cuando una operación utiliza efectivo de la caja física.

El origen externo de fondos identifica cómo se financió una operación; no crea automáticamente una deuda contable con el propietario.

### Cotizaciones y pedidos personalizados

- Creación y edición de cotizaciones, emisión, consulta de estados y documentos.
- Conversión de cotizaciones a pedidos personalizados.
- Pedidos en borrador, confirmación, cambios de estado y seguimiento de cumplimiento.
- Asociación de líneas del pedido con productos y SKU del catálogo.
- Gestión de reservas, anticipos, abonos, pagos finales y reembolsos.
- Conversión del pedido a venta, aplicando los pagos previos para evitar duplicar cobros.
- Historial operativo y financiero del pedido.

### Caja diaria y movimientos de efectivo

- Apertura y cierre por sesión, efectivo esperado, efectivo contado y diferencia.
- Registro de aportes, gastos, retiros y reembolsos.
- Integración con ventas, abonos de clientes, pedidos, compras, proveedores y pagos a empleados.
- Métodos no físicos mostrados separadamente del efectivo del cajón.
- Historial de sesiones, detalle de cada cierre y resumen por día.
- Continuidad desde el último cierre contado, con confirmación explícita o motivo de variación al reabrir.
- Consulta de movimientos por fechas, tipo, origen, método, usuario y texto, con saldo de efectivo posterior a cada movimiento calculable.
- Recuperación de caja abierta tras una interrupción y regularización administrativa de cierres, con motivo y auditoría.
- Aviso al cerrar sesión cuando queda una caja abierta; la salida de la aplicación no cuenta ni cierra el efectivo automáticamente.

Caja diaria presenta por separado el **saldo teórico acumulado**, el **último conteo físico** y una **suma referencial de saldos de sesiones del día**. Esa suma es informativa: no se utiliza como apertura siguiente ni como dinero disponible, porque varias sesiones pueden estar contando el mismo efectivo.

### Empleados y pagos administrativos

- Consulta de actividad, accesos, ventas y unidades por empleado y período.
- Registro e historial de pagos diarios, semanales, quincenales, mensuales o extraordinarios.
- Registro del método y origen de fondos, autorización y anulación auditada de pagos.
- Control del efecto sobre la caja cuando se utilizan fondos físicos.

Este módulo registra pagos administrativos. No implementa una nómina legal, cálculo fiscal, prestaciones obligatorias ni comisiones automáticas.

### Paneles, análisis y reportes

Los paneles presentan indicadores comerciales, financieros y de inventario. Las consultas de rentabilidad utilizan costos históricos disponibles y comunican su cobertura, evitando fabricar información retrospectiva.

El catálogo de reportes avanzados contiene **27 tipos**, distribuidos en seis grupos:

- **Comercial:** ventas, métodos de pago, cotizaciones, pedidos personalizados, devoluciones, reembolsos y anulaciones.
- **Inventario:** productos, rentabilidad por producto, categorías y subcategorías, inventario, movimientos y valor actual del inventario.
- **Financiero:** ganancia bruta, caja, gastos y retiros.
- **Clientes:** directorio, cuentas por cobrar y pagos de clientes.
- **Proveedores:** compras, recepciones, directorio, cuentas por pagar y pagos a proveedores.
- **Auditoría operativa:** operaciones por empleado y actividad administrativa.

Incluye períodos, búsqueda y filtros aplicables a cada reporte, paginación, resúmenes, exportación Excel y vista previa de impresión. La exportación mensual reúne información de ventas, detalle de productos, gastos, retiros e inventario.

Los indicadores apoyan la gestión operativa. No se presentan como un sistema de contabilidad fiscal ni como estados financieros certificados.

### Excel, auditoría y configuración

- Generación de plantilla `.xlsx`, validación de filas, vista previa e importación de productos.
- Generación de códigos cuando corresponde y respaldo previo al proceso de importación.
- Bitácora consultable por fechas, usuario, acción y búsqueda textual sobre los campos admitidos.
- Trazabilidad de acciones mediante usuario, entidad, fecha y descripción.
- Configuración del perfil comercial y de políticas del negocio.
- Configuración local de identidad, modo de estación, rutas y preferencias operativas, separada de la configuración global almacenada en la base.

### Estaciones y continuidad de datos

- **PRINCIPAL:** operación y escritura según los permisos de la cuenta.
- **CONSULTA:** acceso de lectura, con protección transversal en servicios y persistencia; la conexión de base se abre en modo de solo lectura cuando corresponde.
- Identidad propia de estación y protección para conservarla durante restauraciones.
- Respaldos `.aibak` con base de datos, manifiesto y assets canónicos.
- Verificación de integridad, SHA-256, respaldo manual o programado y políticas de retención.
- Restauración con respaldo `PRE_RESTORE`, validación, registro del proceso y rollback.
- Miniaturas tratadas como caché regenerable, fuera de la fuente de verdad de los assets.
- Recuperación en una instalación nueva mediante los flujos de restauración y aprovisionamiento.

El respaldo externo opcional utiliza Amazon S3. El diseño separa una identidad de carga con permisos limitados de las credenciales temporales de recuperación mediante STS y MFA. Las credenciales locales de esa integración se protegen con mecanismos de Windows y su configuración requiere aprovisionamiento administrativo.

### Instalación, actualización y diagnóstico

- Distribución Windows x64 autocontenida, con launcher estable y versiones instaladas lado a lado.
- Separación de ejecutables, datos compartidos, estado de actualización y caché de usuario.
- Paquetes de actualización con manifiesto, comprobación de versión, tamaño y SHA-256, y validaciones de extracción.
- Preparación del paquete y aplicación de operaciones privilegiadas mediante componentes separados.
- Respaldo `PRE_UPDATE`, validaciones de actualización, migraciones y mecanismos de rollback y recuperación.
- Setup de recuperación y bitácora técnica persistente de actualizaciones, independiente de la base comercial.
- Preservación de datos, assets e identidad de estación en los flujos de actualización diseñados para conservar la instalación.

La existencia de estos mecanismos no sustituye la validación operativa del paquete en la versión y el entorno de destino.

## Arquitectura y estructura del repositorio

La solución utiliza una arquitectura por capas, orientada a servicios, contratos y modelos de transferencia. Los formularios coordinan la interacción y llaman a los servicios; las reglas comerciales y la persistencia se concentran fuera de la interfaz.

```text
ArteInventariado/
├── ArteInventariado.Domain/             Entidades, enumeraciones y reglas de dominio
├── ArteInventariado.Application/        Contratos, solicitudes, resultados y modelos
├── ArteInventariado.Infrastructure/     EF Core, servicios, archivos e integraciones
├── ArteInventariado.WinForms/           Formularios, controles, POS y presentación
├── ArteInventariado.Tests/              Pruebas automatizadas
├── ArteInventariado.Launcher/           Entrada estable y selección de versión
├── ArteInventariado.Setup/              Instalación, reparación y recuperación
├── ArteInventariado.Updater/            Aplicación de actualizaciones
├── ArteInventariado.BackupRunner/       Respaldos locales programados
├── ArteInventariado.CloudBackupRunner/  Carga externa de respaldos
├── ArteInventariado.StationHelper/      Operaciones administrativas de estación
└── scripts/                            Desarrollo, publicación y verificación
```

Las dependencias de los proyectos principales siguen esta dirección:

```text
Application    → Domain
Infrastructure → Application + Domain
WinForms       → Application + Infrastructure
Tests          → Application + Infrastructure
```

`WinForms` participa en la composición de dependencias; los servicios concentran las reglas comerciales y el acceso a datos.

## Modelo de datos e integridad

- **Persistencia relacional local:** EF Core, SQLite y migraciones Code First versionadas.
- **Contextos de vida corta:** `IDbContextFactory<AppDbContext>` y un patrón de préstamo y liberación de contextos para evitar compartir entidades rastreadas entre pantallas.
- **Consultas de lectura:** uso de proyecciones y `AsNoTracking` donde corresponde.
- **Transacciones:** coordinación de efectos relacionados, como venta, pagos, existencias y auditoría.
- **Concurrencia:** tokens de versión en saldos y otras entidades operativas, con pruebas de conflictos y actualizaciones obsoletas.
- **Idempotencia:** identificadores de operación y restricciones únicas en los flujos que admiten reintentos protegidos.
- **Dinero:** importes principales almacenados como enteros de centavos para evitar errores de representación binaria en los cálculos monetarios.
- **Históricos:** referencias y snapshots comerciales para conservar información de documentos previos; las correcciones quedan enlazadas y auditadas.
- **Assets:** archivos administrados fuera de la base, incluidos en los respaldos canónicos y con caché de miniaturas separada.
- **Fechas:** tratamiento de registros UTC y períodos locales en las consultas que lo requieren.

La base SQLite se utiliza localmente. Compartir el archivo por una carpeta de red no es un modo soportado de operación.

## Seguridad y límites operativos

Los permisos se comprueban en los servicios, además de condicionar los controles visibles. El modo CONSULTA añade restricciones de escritura independientes del rol de la cuenta.

La instalación para todos los usuarios de Windows utiliza ACL y separación de privilegios. Las operaciones que lo requieren solicitan elevación de Windows; el rol ADMIN de la aplicación no equivale a ser administrador del sistema operativo.

La V1 coordina una sola instancia interactiva por equipo. Dejar la aplicación abierta en una cuenta de Windows y cambiar a otra no habilita dos instancias simultáneas. Cerrar sesión dentro de la aplicación devuelve al formulario de acceso; para liberar la instancia debe cerrarse también la aplicación. La mejora del aviso y el traspaso entre sesiones permanece pendiente.

El hash de contraseñas y las autorizaciones de la aplicación no equivalen a cifrar toda la base SQLite ni todos los respaldos. La protección de los archivos y de sus copias depende también de los permisos y controles del sistema operativo.

## Calidad y pruebas

El repositorio contiene pruebas unitarias y de integración con xUnit para, entre otras áreas:

- Ventas, pagos, descuentos, devoluciones, cambios y anulaciones.
- Crédito, compras, proveedores y pedidos personalizados.
- Inventario canónico, reservas, ubicación, ajustes y concurrencia.
- Caja, continuidad entre sesiones, recuperación y regularización.
- Autorización, modo CONSULTA y límites de persistencia.
- Códigos, etiquetas, imágenes y consultas de catálogo.
- Analítica, reportes, exportación y paginación de impresión.
- Instalación, ACL, actualización, diagnóstico, backup, restauración y recuperación externa.

También existen recorridos de comprobación visual y funcional para formularios, resoluciones y escalas DPI.

La compilación Release se verificó con 0 errores y 0 advertencias. Las pruebas automatizadas no se ejecutaron para esta publicación.

## Compilación y desarrollo

### Requisitos

- Windows con entorno de desarrollo para aplicaciones de escritorio .NET.
- .NET 10 SDK.
- Acceso a las fuentes NuGet configuradas para restaurar dependencias.
- Git y PowerShell para los flujos de trabajo incluidos.

Visual Studio con la carga de trabajo de escritorio .NET es una opción de desarrollo; la compilación también puede realizarse desde la línea de comandos.

Desde la raíz de la solución:

```powershell
dotnet restore ArteInventariado.sln
dotnet build ArteInventariado.sln --configuration Release --no-restore
dotnet test ArteInventariado.Tests/ArteInventariado.Tests.csproj --configuration Release
```

El repositorio dispone de un runner de desarrollo visual:

```powershell
.\scripts\Run-VisualDev.ps1
```

El runner compila y utiliza una raíz VisualDev aislada de la instalación estable. Su inicialización actual requiere una base local existente y puede crear una copia de ella; un clon vacío no incluye un conjunto público de datos de demostración. Para demostraciones y capturas deben prepararse datos sintéticos o debidamente anonimizados.

La distribución autocontenida `win-x64` se prepara mediante los scripts de publicación y empaquetado. Publicar únicamente el ejecutable WinForms no sustituye el paquete completo de instalación y sus componentes auxiliares.

## Pendientes y evolución

### Pendientes de la V1

- Mejorar la respuesta visible cuando otra sesión de Windows mantiene la instancia interactiva abierta y validar el traspaso entre cuentas.
- Mantener alineados el README, las notas de versión, los paquetes, las ramas y los tags de publicación.
- Completar las verificaciones operativas que cada paquete o entorno de instalación tenga pendientes.
- Implementar y certificar adaptadores de impresoras, etiquetadoras y gaveta a partir de dispositivos reales.

### Arquitectura V2 planificada

- API de servicios y base de datos centralizada, con PostgreSQL como opción prevista.
- Acceso desde escritorio, web o móvil.
- Usuarios simultáneos, sincronización y administración remota.
- Evaluación posterior de múltiples dispositivos, sucursales e integraciones.

No se incluyen actualmente una API REST comercial, una aplicación web o móvil ni una base PostgreSQL operativa. Las notificaciones automáticas por correo o mensajería, la facturación electrónica, la nómina legal y las pasarelas de pago tampoco se presentan como funcionalidades implementadas.

## Aspectos técnicos destacados

El proyecto permite mostrar experiencia en desarrollo de software de escritorio y en problemas que van más allá de los formularios:

- Modelado de procesos comerciales y sus relaciones financieras y de inventario.
- Separación de responsabilidades, contratos, DTOs e inyección de dependencias.
- Migración de inventario compatible hacia SKU y ubicaciones con una fuente canónica de saldos.
- Diseño transaccional, concurrencia e idempotencia en operaciones sensibles a reintentos.
- Preservación de históricos y correcciones auditables.
- Autorización por rol, permisos delegados y estaciones de solo lectura.
- Consultas analíticas, gráficos, generación de documentos y exportación estructurada.
- Manejo de archivos y recursos visuales sin cargar originales innecesariamente en los listados.
- Ingeniería de instalación, actualización, privilegios, respaldo, restauración y recuperación ante fallos.
- Pruebas automatizadas sobre flujos comerciales y componentes de infraestructura.
