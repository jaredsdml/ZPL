namespace Zps.Data.Models.Wms;

/// <summary>
/// Empleado leído de t_employee en HighJump (AAD), usado para la autenticación temporal de
/// operarios del módulo de paletización (ver AadWmsService.ValidarCredencialesAsync).
/// EmployeeId viene de t_employee.id (el login/badge que teclea el operario en HighJump),
/// no de t_employee.employee_id (un id interno int, ajeno al login). El esquema real no
/// separa nombre y apellido en dos columnas, solo tiene t_employee.name.
/// </summary>
public sealed record WmsEmployeeModel(
    string EmployeeId,
    string? FullName,
    string? Status);
