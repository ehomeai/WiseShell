FROM ubuntu:24.04
RUN apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends openssh-server \
    && useradd -m -s /bin/bash wiseshelltest && echo 'wiseshelltest:local-test-only' | chpasswd \
    && mkdir /run/sshd && ssh-keygen -A
RUN printf '\nPasswordAuthentication yes\nPermitRootLogin no\nAllowUsers wiseshelltest\n' >> /etc/ssh/sshd_config
EXPOSE 22
CMD ["/usr/sbin/sshd", "-D", "-e"]
