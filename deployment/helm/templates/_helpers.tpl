{{/*
Expand the name of the chart.
*/}}
{{- define "web-api.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Create a default fully qualified app name.
We truncate at 63 chars because some Kubernetes name fields are limited to this (by the DNS naming spec).
If release name contains chart name it will be used as a full name.
*/}}
{{- define "web-api.fullname" -}}
{{- if .Values.fullnameOverride }}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- $name := default .Chart.Name .Values.nameOverride }}
{{- if contains $name .Release.Name }}
{{- .Release.Name | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- end }}
{{- end }}

{{/*
Create chart name and version as used by the chart label.
*/}}
{{- define "web-api.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Common labels
*/}}
{{- define "web-api.labels" -}}
helm.sh/chart: {{ include "web-api.chart" . }}
{{ include "web-api.selectorLabels" . }}
{{- if .Chart.AppVersion }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
{{- end }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end }}

{{/*
Selector labels
*/}}
{{- define "web-api.selectorLabels" -}}
app.kubernetes.io/name: {{ include "web-api.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
azure.workload.identity/use: "true"
{{- end }}

{{/*
Create the name of the service account to use
*/}}
{{- define "web-api.serviceAccountName" -}}
{{- if .Values.serviceAccount.create }}
{{- default (include "web-api.fullname" .) .Values.serviceAccount.name }}
{{- else }}
{{- default "default" .Values.serviceAccount.name }}
{{- end }}
{{- end }}

{{/*
Resolve the public host used by the AGFC HTTPRoutes.
*/}}
{{- define "web-api.host" -}}
{{- default (printf "%s%s" .Values.subDomain .Values.domain) .Values.agfc.route.host -}}
{{- end }}

{{/*
Resolve the external path prefix matched by the AGFC HTTPRoutes.
*/}}
{{- define "web-api.pathPrefix" -}}
{{- default .Values.constConfig.virtualDirectory .Values.agfc.route.pathPrefix -}}
{{- end }}

{{/*
Container for a pipeline role (Lister, DownloadWorker, ProcessWorker). Same image and settings as the API pod.
Call with (dict "root" $ "role" "Lister" "resources" .Values.pipeline.lister.resources).
*/}}
{{- define "web-api.pipelineContainer" -}}
{{- $root := .root -}}
{{- $fullname := include "web-api.fullname" $root -}}
- name: {{ $fullname }}-{{ .role | lower }}
  image: "{{ $root.Values.image.repository }}:{{ $root.Values.image.tag | default $root.Chart.AppVersion }}"
  imagePullPolicy: {{ $root.Values.image.pullPolicy }}
  securityContext:
    {{- toYaml $root.Values.securityContext | nindent 4 }}
  envFrom:
  - configMapRef:
      name: {{ $fullname }}-inbound-config
  - configMapRef:
      name: {{ $fullname }}-pipeline-config
  env:
  - name: Pipeline__Role
    value: {{ .role | quote }}
  - name: ASPNETCORE_ENVIRONMENT
    valueFrom:
      configMapKeyRef:
        name: {{ $fullname }}-config
        key: AspEnv
  - name: APPLICATIONINSIGHTS_CONNECTION_STRING
    valueFrom:
      configMapKeyRef:
        name: {{ $fullname }}-config
        key: AppInsightsConnection
  - name: APPINSIGHTS_INSTRUMENTATIONKEY
    valueFrom:
      configMapKeyRef:
        name: {{ $fullname }}-config
        key: AppInsightKey
  - name: KEY_VAULT_NAME
    valueFrom:
      configMapKeyRef:
        name: {{ $fullname }}-config
        key: KvName
  - name: AZURE_CLIENT_ID
    valueFrom:
      configMapKeyRef:
        name: {{ $fullname }}-config
        key: AzClientId
  - name: DOTNET_SYSTEM_GLOBALIZATION_INVARIANT
    value: "false"
  {{- if and $root.Values.datadog $root.Values.datadog.enabled }}
  - name: DD_SERVICE
    value: "{{ $root.Values.datadog.service }}"
  - name: DD_ENV
    value: "{{ $root.Values.datadog.env }}"
  {{- end }}
  resources:
    {{- toYaml .resources | nindent 4 }}
{{- end }}
