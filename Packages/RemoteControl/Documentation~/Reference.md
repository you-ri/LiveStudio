# Reference — Lilium Remote Control

API reference for the `[Exposed*]` attributes and the REST endpoints served by `RemoteControlServerManager`.

The README's Quick Start covers the common case. This document is for cases where you need the full surface — writing a custom remote client, deciding which attribute to use, or debugging an unexpected response.

---

## Attributes

| Attribute | Target | Purpose |
|---|---|---|
| `[ExposedClass]` | class / struct | Marks a type as remotely exposable. Required on any type whose members are meant to surface in the remote client. |
| `[ExposedProperty]` | property / field | Surfaces the member for remote get/set. |
| `[ExposedFunction]` | method | Allows the method to be invoked remotely. |
| `[ExposedEnum]` | enum | Publishes the enum's type definition so the remote client can render a dropdown. |
| `[ExposedHelp("text")]` | any | Attaches help / description text. The string is treated as a localization key — see [Localization.md](Localization.md). |
| `[Slider(min, max)]` | property | Hints to the remote client that this property should be drawn as a slider with the given range. |
| `[ExposedDefault]` | static property | Provides a custom default value for a struct (used for `Reset` semantics). |
| `ExposedPropertyRef` | field type | A `readonly struct` that aliases an `ExposedProperty` declared on another component. Useful for aggregation pages that surface properties from multiple components in one place. Value, dirty state, and revert all delegate to the referenced property. See [ExposedObjectSpec.md](ExposedObjectSpec.md) for details. |

---

## REST API endpoints

All endpoints are served by `HttpServerCore` under the configured base URL (default `http://localhost:9095`).

### System

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/status` | Application status (name, version, FPS). |
| `GET` | `/api/stream` | SSE event stream subscription. |
| `GET` / `POST` | `/api/heartbeat` | Connection-keepalive heartbeat. |

### Exposed objects

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/exposed/objects` | List all exposed objects. |
| `GET` | `/exposed/objects?type={typeName}` | Filter exposed objects by type. |
| `GET` | `/exposed/object/{id}` | Fetch a single exposed object by id. |
| `GET` | `/exposed/object/{id}/{path}` | Read a property value. |
| `PUT` | `/exposed/object/{id}/{path}` | Write a property value. |
| `POST` | `/exposed/object/{id}/{path}` | Append an array element. |
| `DELETE` | `/exposed/object/{id}/{path}` | Remove an array element. |
| `POST` | `/exposed/object/{id}/{path}/reset` | Reset a property to its default. |

### Type definitions

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/exposed/types` | List all exposed type definitions. |
| `GET` | `/exposed/types?type={typeName}` | Fetch a specific type definition. |
| `GET` | `/exposed/enums` | List all exposed enum definitions. |

### Functions

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/exposed/function/{id}/{functionName}` | Invoke an `[ExposedFunction]` method. |

### Persistence

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/exposed/export` | Export current settings to a file. |
| `POST` | `/exposed/import` | Import settings from a file. |

### Localization

See [Localization.md](Localization.md) for `/api/language`.
