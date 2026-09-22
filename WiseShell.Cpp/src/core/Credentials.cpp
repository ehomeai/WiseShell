#include "Credentials.h"
#include <qtkeychain/keychain.h>
namespace wise {
void Credentials::read(const QString &id, SecretResult done) {
    if (cache_.contains(id)) {
        done(cache_.value(id), {});
        return;
    }
    if (invalid_.contains(id)) {
        done({}, {});
        return;
    }
    auto *job = new QKeychain::ReadPasswordJob("WiseShellCpp", this);
    job->setKey(id);
    job->setInsecureFallback(false);
    connect(job, &QKeychain::Job::finished, this, [this, job, id, done](QKeychain::Job *) {
        if (job->error() == QKeychain::NoError) {
            cache_[id] = job->textData();
            done(job->textData(), {});
        } else
            done({}, job->error() == QKeychain::EntryNotFound ? QString() : job->errorString());
    });
    job->start();
}
void Credentials::write(const QString &id, const QString &secret, SecretResult done) {
    cache_[id] = secret;
    invalid_.remove(id);
    auto *job = new QKeychain::WritePasswordJob("WiseShellCpp", this);
    job->setKey(id);
    job->setTextData(secret);
    job->setInsecureFallback(false);
    connect(job, &QKeychain::Job::finished, this,
            [job, done](QKeychain::Job *) { done({}, job->error() ? job->errorString() : QString()); });
    job->start();
}
void Credentials::remove(const QString &id, SecretResult done) {
    cache_.remove(id);
    auto *job = new QKeychain::DeletePasswordJob("WiseShellCpp", this);
    job->setKey(id);
    job->setInsecureFallback(false);
    connect(job, &QKeychain::Job::finished, this, [job, done](QKeychain::Job *) {
        done({}, job->error() && job->error() != QKeychain::EntryNotFound ? job->errorString() : QString());
    });
    job->start();
}
void Credentials::invalidate(const QString &id) {
    cache_.remove(id);
    invalid_.insert(id);
    // An authentication failure means a remembered secret is no longer trusted.
    // Remove it from the platform store as well as from the in-memory cache.
    auto *job = new QKeychain::DeletePasswordJob("WiseShellCpp", this);
    job->setKey(id);
    job->setInsecureFallback(false);
    connect(job, &QKeychain::Job::finished, job, &QObject::deleteLater);
    job->start();
}
} // namespace wise
