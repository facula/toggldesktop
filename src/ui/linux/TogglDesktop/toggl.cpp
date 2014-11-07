// Copyright 2014 Toggl Desktop developers.

#include "./toggl.h"

#include <QStandardPaths>
#include <QDir>
#include <QCoreApplication>
#include <QDesktopServices>
#include <QDebug>
#include <QApplication>
#include <QStringList>
#include <QDate>

#include <iostream>   // NOLINT

#include "./../../../lib/include/toggl_api.h"

#include "./updateview.h"
#include "./timeentryview.h"
#include "./genericview.h"
#include "./autocompleteview.h"
#include "./settingsview.h"
#include "./bugsnag.h"

TogglApi *TogglApi::instance = 0;

QString TogglApi::Project = QString("project");
QString TogglApi::Duration = QString("duration");
QString TogglApi::Description = QString("description");

void on_display_app(const _Bool open) {
    TogglApi::instance->displayApp(open);
}

void on_display_error(
    const char *errmsg,
    const _Bool user_error) {
    TogglApi::instance->displayError(QString(errmsg), user_error);
}

void on_display_update(
    const _Bool open,
    TogglUpdateView *update) {
    TogglApi::instance->displayUpdate(open,
                                      UpdateView::importOne(update));
}

void on_display_online_state(
    const bool is_online,
    const char *reason) {
    TogglApi::instance->displayOnlineState(is_online,
                                           QString(reason));
}

void on_display_url(
    const char *url) {
    QDesktopServices::openUrl(QUrl(url));
}

void on_display_login(
    const _Bool open,
    const uint64_t user_id) {
    TogglApi::instance->displayLogin(open, user_id);
    Bugsnag::user.id = user_id;
}

void on_display_reminder(
    const char *title,
    const char *informative_text) {
    TogglApi::instance->displayReminder(
        QString(title),
        QString(informative_text));
}

void on_display_time_entry_list(
    const _Bool open,
    TogglTimeEntryView *first) {
    TogglApi::instance->displayTimeEntryList(
        open,
        TimeEntryView::importAll(first));
}

void on_display_time_entry_autocomplete(
    TogglAutocompleteView *first) {
    TogglApi::instance->displayTimeEntryAutocomplete(
        AutocompleteView::importAll(first));
}

void on_display_project_autocomplete(
    TogglAutocompleteView *first) {
    TogglApi::instance->displayProjectAutocomplete(
        AutocompleteView::importAll(first));
}

void on_display_workspace_select(
    TogglGenericView *first) {
    TogglApi::instance->displayWorkspaceSelect(
        GenericView::importAll(first));
}

void on_display_client_select(
    TogglGenericView *first) {
    TogglApi::instance->displayClientSelect(
        GenericView::importAll(first));
}

void on_display_tags(
    TogglGenericView *first) {
    TogglApi::instance->displayTags(
        GenericView::importAll(first));
}

void on_display_time_entry_editor(
    const _Bool open,
    TogglTimeEntryView *te,
    const char *focused_field_name) {
    TogglApi::instance->displayTimeEntryEditor(
        open,
        TimeEntryView::importOne(te),
        QString(focused_field_name));
}

void on_display_settings(
    const _Bool open,
    TogglSettingsView *settings) {
    TogglApi::instance->displaySettings(
        open,
        SettingsView::importOne(settings));
}

void on_display_timer_state(
    TogglTimeEntryView *te) {
    if (te) {
        TogglApi::instance->displayRunningTimerState(
            TimeEntryView::importOne(te));
        return;
    }

    TogglApi::instance->displayStoppedTimerState();
}

void on_display_idle_notification(
    const char *guid,
    const char *since,
    const char *duration,
    const uint64_t started) {
    TogglApi::instance->displayIdleNotification(
        QString(guid),
        QString(since),
        QString(duration),
        started);
}

TogglApi::TogglApi(QObject *parent)
    : QObject(parent)
, shutdown(false)
, ctx(0) {
    QString version = QApplication::applicationVersion();
    ctx = toggl_context_init("linux_native_app",
                             version.toStdString().c_str());

    QString appDirPath =
        QStandardPaths::writableLocation(
            QStandardPaths::DataLocation);
    QDir appDir = QDir(appDirPath);
    if (!appDir.exists()) {
        appDir.mkpath(".");
    }

    QString logPath = appDir.filePath("toggldesktop.log");
    toggl_set_log_path(logPath.toUtf8().constData());
    qDebug() << "Log path " << logPath;

    toggl_set_log_level("debug");

    QString dbPath = appDir.filePath("toggldesktop.db");
    toggl_set_db_path(ctx, dbPath.toUtf8().constData());
    qDebug() << "DB path " << dbPath;

    QString executablePath = QCoreApplication::applicationDirPath();
    QDir executableDir = QDir(executablePath);
    QString cacertPath = executableDir.filePath("cacert.pem");
    toggl_set_cacert_path(ctx, cacertPath.toUtf8().constData());

    toggl_on_show_app(ctx, on_display_app);
    toggl_on_error(ctx, on_display_error);
    toggl_on_update(ctx, on_display_update);
    toggl_on_online_state(ctx, on_display_online_state);
    toggl_on_url(ctx, on_display_url);
    toggl_on_login(ctx, on_display_login);
    toggl_on_reminder(ctx, on_display_reminder);
    toggl_on_time_entry_list(ctx, on_display_time_entry_list);
    toggl_on_time_entry_autocomplete(ctx, on_display_time_entry_autocomplete);
    toggl_on_project_autocomplete(ctx, on_display_project_autocomplete);
    toggl_on_workspace_select(ctx, on_display_workspace_select);
    toggl_on_client_select(ctx, on_display_client_select);
    toggl_on_tags(ctx, on_display_tags);
    toggl_on_time_entry_editor(ctx, on_display_time_entry_editor);
    toggl_on_settings(ctx, on_display_settings);
    toggl_on_timer_state(ctx, on_display_timer_state);
    toggl_on_idle_notification(ctx, on_display_idle_notification);

    char *env = toggl_environment(ctx);
    if (env) {
    	Bugsnag::releaseStage = QString(env);
	free(env);
    }

    instance = this;
}

TogglApi::~TogglApi() {
    toggl_context_clear(ctx);
    ctx = 0;

    instance = 0;
}

bool TogglApi::notifyBugsnag(
  const QString errorClass,
  const QString message,
  const QString context) {
        QHash<QString, QHash<QString, QString> > metadata;
	if (instance) {
		metadata["release"]["channel"] = instance->updateChannel();
	}
	return Bugsnag::notify(errorClass, message, context, &metadata);
}

bool TogglApi::startEvents() {
    return toggl_ui_start(ctx);
}

void TogglApi::login(const QString email, const QString password) {
    toggl_login(ctx,
                email.toStdString().c_str(),
                password.toStdString().c_str());
}

void TogglApi::setEnvironment(const QString environment) {
    toggl_set_environment(ctx, environment.toStdString().c_str());
    Bugsnag::releaseStage = environment;
}

bool TogglApi::setTimeEntryDate(
    const QString guid,
    const int64_t unix_timestamp) {
    return toggl_set_time_entry_date(ctx,
                                     guid.toStdString().c_str(),
                                     unix_timestamp);
}

bool TogglApi::setTimeEntryStart(
    const QString guid,
    const QString value) {
    return toggl_set_time_entry_start(ctx,
                                      guid.toStdString().c_str(),
                                      value.toStdString().c_str());
}

bool TogglApi::setTimeEntryStop(
    const QString guid,
    const QString value) {
    return toggl_set_time_entry_end(ctx,
                                    guid.toStdString().c_str(),
                                    value.toStdString().c_str());
}

void TogglApi::googleLogin(const QString accessToken) {
    toggl_google_login(ctx, accessToken.toStdString().c_str());
}

bool TogglApi::setProxySettings(
    const bool useProxy,
    const QString proxyHost,
    const uint64_t proxyPort,
    const QString proxyUsername,
    const QString proxyPassword) {
    return toggl_set_proxy_settings(ctx,
                                    useProxy,
                                    proxyHost.toStdString().c_str(),
                                    proxyPort,
                                    proxyUsername.toStdString().c_str(),
                                    proxyPassword.toStdString().c_str());
}

bool TogglApi::discardTimeAt(const QString guid,
                             const uint64_t at,
                             const bool split_into_new_time_entry) {
    return toggl_discard_time_at(ctx,
                                 guid.toStdString().c_str(),
                                 at,
                                 split_into_new_time_entry);
}

void TogglApi::setIdleSeconds(u_int64_t idleSeconds) {
    toggl_set_idle_seconds(ctx, idleSeconds);
}

bool TogglApi::setSettings(const bool useIdleDetection,
                           const bool menubarTimer,
                           const bool dockIcon,
                           const bool onTop,
                           const bool reminder,
                           const uint64_t idleMinutes) {
    return toggl_set_settings(ctx,
                              useIdleDetection,
                              menubarTimer,
                              dockIcon,
                              onTop,
                              reminder,
                              idleMinutes);
}

void TogglApi::toggleTimelineRecording(const bool recordTimeline) {
    toggl_timeline_toggle_recording(ctx, recordTimeline);
}

bool TogglApi::setUpdateChannel(const QString channel) {
    return toggl_set_update_channel(ctx, channel.toStdString().c_str());
}

QString TogglApi::updateChannel() {
     char *channel = toggl_get_update_channel(ctx);
	QString res;
     if (channel) {
	res = QString(channel);
	free(channel);
	}
	return res;
}

QString TogglApi::start(
    const QString description,
    const QString duration,
    const uint64_t task_id,
    const uint64_t project_id) {
    char *guid = toggl_start(ctx,
                             description.toStdString().c_str(),
                             duration.toStdString().c_str(),
                             task_id,
                             project_id);
    QString res("");
    if (guid) {
        res = QString(guid);
        free(guid);
    }
    return res;
}

bool TogglApi::stop() {
    return toggl_stop(ctx);
}

const QString TogglApi::formatDurationInSecondsHHMMSS(
    const int64_t duration) {
    char *buf = toggl_format_duration_in_seconds_hhmmss(duration);
    QString res = QString(buf);
    free(buf);
    return res;
}

bool TogglApi::continueTimeEntry(const QString guid) {
    return toggl_continue(ctx, guid.toStdString().c_str());
}

bool TogglApi::continueLatestTimeEntry() {
    return toggl_continue_latest(ctx);
}

void TogglApi::sync() {
    toggl_sync(ctx);
}

void TogglApi::openInBrowser() {
    toggl_open_in_browser(ctx);
}

void TogglApi::about() {
    toggl_about(ctx);
}

bool TogglApi::clearCache() {
    return toggl_clear_cache(ctx);
}

void TogglApi::getSupport() {
    toggl_get_support(ctx);
}

void TogglApi::logout() {
    toggl_logout(ctx);
}

void TogglApi::editPreferences() {
    toggl_edit_preferences(ctx);
}

void TogglApi::editTimeEntry(const QString guid,
                             const QString focusedFieldName) {
    toggl_edit(ctx,
               guid.toStdString().c_str(),
               false,
               focusedFieldName.toStdString().c_str());
}

bool TogglApi::setTimeEntryProject(
    const QString guid,
    const uint64_t task_id,
    const uint64_t project_id,
    const QString project_guid) {
    return toggl_set_time_entry_project(ctx,
                                        guid.toStdString().c_str(),
                                        task_id,
                                        project_id,
                                        project_guid.toStdString().c_str());
}

bool TogglApi::setTimeEntryDescription(
    const QString guid,
    const QString value) {
    return toggl_set_time_entry_description(ctx,
                                            guid.toStdString().c_str(),
                                            value.toStdString().c_str());
}

bool TogglApi::setTimeEntryTags(
    const QString guid,
    const QString tags) {
    return toggl_set_time_entry_tags(ctx,
                                     guid.toStdString().c_str(),
                                     tags.toStdString().c_str());
}

void TogglApi::editRunningTimeEntry(
    const QString focusedFieldName) {
    toggl_edit(ctx,
               "",
               true,
               focusedFieldName.toStdString().c_str());
}

bool TogglApi::setTimeEntryBillable(
    const QString guid,
    const bool billable) {
    return toggl_set_time_entry_billable(ctx,
                                         guid.toStdString().c_str(),
                                         billable);
}

bool TogglApi::addProject(
    const QString time_entry_guid,
    const uint64_t workspace_id,
    const uint64_t client_id,
    const QString project_name,
    const bool is_private) {
    return toggl_add_project(ctx,
                             time_entry_guid.toStdString().c_str(),
                             workspace_id,
                             client_id,
                             project_name.toStdString().c_str(),
                             is_private);
}

void TogglApi::viewTimeEntryList() {
    toggl_view_time_entry_list(ctx);
}

bool TogglApi::deleteTimeEntry(const QString guid) {
    return toggl_delete_time_entry(ctx, guid.toStdString().c_str());
}

bool TogglApi::setTimeEntryDuration(
    const QString guid,
    const QString value) {
    return toggl_set_time_entry_duration(ctx,
                                         guid.toStdString().c_str(),
                                         value.toStdString().c_str());
}

bool TogglApi::sendFeedback(const QString topic,
                            const QString details,
                            const QString filename) {
    return toggl_feedback_send(ctx,
                               topic.toStdString().c_str(),
                               details.toStdString().c_str(),
                               filename.toStdString().c_str());
}
