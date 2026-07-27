# Reference — Lilium Remote Control

API reference for the `[Exposed*]` attributes and the REST endpoints served by `RemoteControlServerManager`.

The README's Quick Start covers the common case. This document is for cases where you need the full surface — writing a custom remote client, deciding which attribute to use, or debugging an unexpected response.

---

## Attributes

| Attribute | Target | Purpose |
|---|---|---|
| `[ExposedClass]` | class / struct | Marks a type as remotely exposable. Required on any type whose members are meant to surface in the remote client. |
| `[ExposedProperty]` | property / field | Surfaces the member for remote get/set. |
| `[ExposedFunction]` | method | Allows the method to be invoked remotely. `icon` adds a Material Icons glyph to the button. |
| `[ExposedEnum]` | enum | Publishes the enum's type definition so the remote client can render a dropdown. |
| `[ExposedHelp("text")]` | any | Attaches help / description text. The string is treated as a localization key — see [Localization.md](Localization.md). |
| `[Slider(min, max)]` | property | Hints to the remote client that this property should be drawn as a slider with the given range. |
| `[Section(icon, title)]` | property / field / method | Starts a titled section. Following members belong to it until the next `[Section]`. Allowed on a method so a section can consist only of buttons. |
| `[Countdown(seconds)]` | method | Delays the invocation: the client opens a modal, counts down, then calls the function — giving the operator time to get into position. Cancelling never reaches the server. `seconds = 0` defers to the client's default. `message` / `runningMessage` are localization keys, `icon` is a Material Icons name. |
| `[UrlButton]` | property / field | Renders a read-only `string` member as a button that opens its value in the operator's external browser. The caption comes from the member's label. |
| `[Layout("path")]` | property / field / method | Groups members into a layout container so the client arranges them horizontally instead of stacking them. `/` in the path nests groups (`"row/left"`), so no end marker is needed. Direction defaults to `Auto`, which alternates by depth (a section stacks vertically, so depth 1 is horizontal, depth 2 vertical, …); set `direction` to pin it. `columns = N` lays the group out as an N-column grid, and `grow` weights how much it stretches against its siblings. |
| `[ExposedDefault]` | static property | Provides a custom default value for a struct (used for `Reset` semantics). |
| `ExposedPropertyRef` | field type | A `readonly struct` that aliases an `ExposedProperty` declared on another component. Useful for aggregation pages that surface properties from multiple components in one place. Value, dirty state, and revert all delegate to the referenced property. See [ExposedObjectSpec.md](ExposedObjectSpec.md) for details. |

---

## REST API endpoints

All endpoints are served by `HttpServerCore` under the configured base URL (default `http://localhost:9095`).

### System

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/status` | Application status (name, version, FPS). |
| `GET` | `/api/stream` | SSE event stream subscription (notifications and confirmations only). |
| `GET` / `POST` | `/api/heartbeat` | Connection-keepalive heartbeat. |

### Exposed objects

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/exposed/objects` | List all exposed objects (depth 1; add `?nested` for full expansion). |
| `GET` | `/exposed/objects?type={typeName}` | Filter exposed objects by type. |
| `GET` | `/exposed/object/{id}` | Fetch a single exposed object by id (depth 1; add `?nested` for full expansion). |
| `GET` | `/exposed/object/{id}/{path}` | Read a property value (always fully expanded). |
| `PUT` | `/exposed/object/{id}/{path}` | Write a property value. |
| `POST` | `/exposed/object/{id}/{path}` | Append an array element. |
| `DELETE` | `/exposed/object/{id}/{path}` | Remove an array element. |
| `POST` | `/exposed/object/{id}/{path}/reset` | Reset a property to its default. |
| `GET` | `/exposed/changes` | Current change revision (no ids). Used to sync up on connect. |
| `GET` | `/exposed/changes?since={revision}` | Ids of objects changed after `{revision}`. |

Property changes are not pushed. The server records only the **id** of each changed object, and a
client polls `/exposed/changes` to learn what to refetch — so nobody receives data for a page they are
not looking at, and a change costs the same whether zero or five clients are connected. The pseudo ids
`@types` and `@ui` mean "refetch the type tables" and "refetch the side menu" respectively.

By default an object is returned at **depth 1**: its own property values are serialized, but a nested
inline (unregistered) composite child is replaced by a truncation stub
`{ "@type": ..., "@truncated": true }` so the list stays small and scalable — fetch the child on demand
via `GET /exposed/object/{id}/{path}`. Arrays do not consume depth (element count and type stay
visible), and registered children keep their `@ref` form. Pass `?nested` (or `?nested=true`) to restore
full unbounded expansion. Property reads, PUT responses and persistence are always fully expanded.

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
